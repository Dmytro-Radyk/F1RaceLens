using F1RaceLens.Api.Calendar;
using F1RaceLens.Api.Standings;

namespace F1RaceLens.Api.Predictions;

public sealed class PredictionService(CalendarService calendars, StandingsService standings,
    PredictionSource source, ForecastWeatherClient weather, TimeProvider clock, ILogger<PredictionService> logger)
{
    private PredictionResponse? cached;
    private ForecastInput? cachedInput;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (DateTimeOffset Fetched, IReadOnlyList<ForecastObservation> Rows)> priorCache = new();
    private DateTimeOffset refreshAfter;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<PredictionResponse> GetAsync(CancellationToken token)
    {
        var year = clock.GetUtcNow().Year;
        if (Fresh(year)) return cached!;
        await refreshLock.WaitAsync(token);
        try
        {
            if (Fresh(year)) return cached!;
            try
            {
                var started = clock.GetUtcNow();
                var season = year.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var calendarTask = calendars.GetAsync(season, token);
                var driverTask = standings.GetAsync(season, "drivers", token);
                var teamTask = standings.GetAsync(season, "constructors", token);
                var raceTask = source.FetchAsync(year, false, token);
                var sprintTask = source.FetchAsync(year, true, token);
                var priorTask = Task.WhenAll(LoadPrior(year - 1, token), LoadPrior(year - 2, token));
                await Task.WhenAll(calendarTask, driverTask, teamTask, raceTask, sprintTask, priorTask);
                var calendar = await calendarTask; var drivers = await driverTask; var teams = await teamTask;
                var history = (await raceTask).Concat(await sprintTask).ToArray();
                var timestamp = new[] { started, calendar.UpdatedAt, drivers.UpdatedAt, teams.UpdatedAt }.Min();
                var stale = calendar.IsStale || drivers.IsStale || teams.IsStale;
                var prior = (await priorTask).SelectMany(rows => rows).ToArray();
                var events = await weather.EnrichAsync(FutureEvents(calendar, drivers.Round ?? 0), token);
                cached = Build(year, timestamp, stale, calendar, drivers, teams, history, token, prior, events);
                cachedInput = cached.Status == "ready" ? MakeInput(calendar, drivers, teams, history, prior, events) : null;
                refreshAfter = clock.GetUtcNow().AddMinutes(cached.Status is "ready" or "completed" ? 15 : 1);
                return cached;
            }
            catch (Exception error) when (error is PredictionSourceException or CalendarSourceException or StandingsSourceException)
            {
                if (cached is not null && cached.Season == year && cached.UpdatedAt.AddHours(24) > clock.GetUtcNow())
                {
                    logger.LogWarning(error, "Serving cached season predictions.");
                    cached = cached with { IsStale = true };
                    refreshAfter = clock.GetUtcNow().AddMinutes(1);
                    return cached;
                }
                throw new PredictionSourceException("Prediction inputs unavailable.", error);
            }
        }
        finally { refreshLock.Release(); }
    }
    private bool Fresh(int year) => cached is not null && cached.Season == year && refreshAfter > clock.GetUtcNow()
        && cached.UpdatedAt.AddHours(24) > clock.GetUtcNow();

    private async Task<IReadOnlyList<ForecastObservation>> LoadPrior(int year, CancellationToken token)
    {
        if (priorCache.TryGetValue(year, out var saved) && saved.Fetched.AddHours(24) > clock.GetUtcNow()) return saved.Rows;
        try
        {
            var rows = await source.FetchAsync(year, false, token);
            priorCache[year] = (clock.GetUtcNow(), rows); return rows;
        }
        catch (PredictionSourceException error) { logger.LogWarning(error, "Optional circuit history unavailable for {Year}.", year); return []; }
    }
    public static IReadOnlyList<ForecastEvent> FutureEvents(CalendarResponse calendar, int throughRound) => calendar.Races.Where(race => race.Round > throughRound)
        .SelectMany(race => race.Sessions.Where(session => session.Kind is "Race" or "Sprint").Select(session => new ForecastEvent(
            race.Round, session.Kind == "Sprint", race.Circuit.Id, race.Name, session.Date, session.StartsAt, race.Circuit.Latitude, race.Circuit.Longitude)))
        .OrderBy(item => item.Round).ThenByDescending(item => item.Sprint).ToArray();
    private static ForecastInput MakeInput(CalendarResponse calendar, StandingsResponse drivers, StandingsResponse teams,
        IReadOnlyList<ForecastObservation> history, IReadOnlyList<ForecastObservation>? prior, IReadOnlyList<ForecastEvent>? events)
    {
        var remaining = calendar.Races.Where(race => race.Round > (drivers.Round ?? 0)).ToArray();
        return new(drivers.Entries, teams.Entries, history, remaining.Length,
            remaining.Count(race => race.Sessions.Any(session => session.Kind == "Sprint")), events, prior);
    }
    public async Task<PredictionResponse> ScenarioAsync(ForecastScenario scenario, CancellationToken token)
    {
        var baseline = await GetAsync(token);
        await refreshLock.WaitAsync(token);
        try
        {
            if (cachedInput is null || cached?.Status != "ready") return baseline;
            PredictionContext.ValidateScenario(cachedInput, scenario);
            var input = cachedInput with { Scenario = scenario };
            var output = PredictionEngine.Simulate(input, token: token);
            return cached with { Drivers = output.Drivers, Teams = output.Teams, Factors = PredictionContext.Describe(input), Races = output.Races };
        }
        finally { refreshLock.Release(); }
    }

    public static PredictionResponse Build(int year, DateTimeOffset updatedAt, bool stale, CalendarResponse calendar,
        StandingsResponse drivers, StandingsResponse teams, IReadOnlyList<ForecastObservation> history, CancellationToken token = default,
        IReadOnlyList<ForecastObservation>? prior = null, IReadOnlyList<ForecastEvent>? events = null)
    {
        var round = drivers.Round ?? 0;
        var remaining = calendar.Races.Where(race => race.Round > round).ToArray();
        var remainingSprints = remaining.Count(race => race.Sessions.Any(session => session.Kind == "Sprint"));
        PredictionResponse Pending(string status, string message) => new(year, updatedAt, stale, "Jolpica", status, message,
            round, remaining.Length, remainingSprints, 0, PredictionEngine.Version, [], []);
        if (year != 2026) return Pending("unsupported-season", "Ця версія моделі перевірена для правил сезону 2026. Потрібно оновити правила перед новим сезоном.");
        if (history.Count(row => !row.Sprint) == 0 || drivers.Entries.Count < 2 || teams.Entries.Count < 2)
            return Pending("insufficient-data", "Для прогнозу потрібні заліки та щонайменше три завершені Гран-прі поточного сезону.");
        if (calendar.Season != year || drivers.Season != year || teams.Season != year || teams.Round != drivers.Round
            || history.Any(row => row.Round > round) || history.Where(row => !row.Sprint).Select(row => row.Round).DefaultIfEmpty().Max() != round)
            return Pending("synchronizing", "Календар, заліки й результати ще не узгоджені. Прогноз з’явиться після оновлення джерела.");
        var expectedRaces = calendar.Races.Where(race => race.Round <= round).Select(race => race.Round).Order().ToArray();
        var actualRaces = history.Where(row => !row.Sprint).Select(row => row.Round).Distinct().Order().ToArray();
        var expectedSprints = calendar.Races.Where(race => race.Round <= round && race.Sessions.Any(session => session.Kind == "Sprint")).Select(race => race.Round).Order().ToArray();
        var actualSprints = history.Where(row => row.Sprint).Select(row => row.Round).Distinct().Order().ToArray();
        if (!expectedRaces.SequenceEqual(actualRaces) || !expectedSprints.SequenceEqual(actualSprints)
            || history.GroupBy(row => (row.Round, row.Sprint)).Any(group => group.Count() < 10
                || group.Select(row => row.DriverId).Distinct().Count() != group.Count()
                || group.Select(row => row.Position).Distinct().Count() != group.Count()
                || group.Max(row => row.Position) != group.Count())
            || remaining.Any(race => race.Date < DateOnly.FromDateTime(updatedAt.UtcDateTime)))
            return Pending("synchronizing", "У джерелі бракує результатів проведених сесій або календар ще оновлюється. Неповні дані не використовуються для прогнозу.");
        if (actualRaces.Length < 3) return Pending("insufficient-data", "Потрібні щонайменше три завершені Гран-прі для оцінки форми та розкиду результатів.");
        var entries = drivers.Entries.Concat(teams.Entries).ToArray();
        if (entries.Any(entry => entry.Position is null || !int.TryParse(entry.PositionText, out _)))
            return Pending("unsupported-standings", "Залік містить виключення або неоднозначні місця, які ця версія моделі ще не підтримує.");
        var roster = history.Where(row => !row.Sprint && row.Round == round).ToArray();
        if (roster.Any(row => drivers.Entries.All(driver => driver.Id != row.DriverId) || teams.Entries.All(team => team.Id != row.TeamId)))
            return Pending("synchronizing", "Склад останнього Гран-прі ще не узгоджений із заліками.");
        var count = remaining.Length + remainingSprints == 0 ? 1 : 4000;
        var input = MakeInput(calendar, drivers, teams, history, prior, events);
        var output = PredictionEngine.Simulate(input, count, token: token);
        return new PredictionResponse(year, updatedAt, stale, "Jolpica", remaining.Length == 0 ? "completed" : "ready", null,
            round, remaining.Length, remainingSprints, count, PredictionEngine.Version, output.Drivers, output.Teams, PredictionContext.Describe(input), output.Races);
    }
    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/predictions/current", async (
        PredictionService service, HttpContext context, ILogger<PredictionService> logger) => {
        try { return Results.Ok(await service.GetAsync(context.RequestAborted)); }
        catch (PredictionSourceException error)
        {
            logger.LogWarning(error, "Season prediction request failed.");
            context.Response.Headers.RetryAfter = "60";
            return Results.Problem(statusCode: 503, title: "Дані для прогнозу тимчасово недоступні.");
        }
        });
        app.MapPost("/api/predictions/scenario", async (ForecastScenario scenario, PredictionService service, HttpContext context, ILogger<PredictionService> logger) => {
            try { return Results.Ok(await service.ScenarioAsync(scenario, context.RequestAborted)); }
            catch (ArgumentException error) { return Results.Problem(statusCode: 400, title: error.Message); }
            catch (PredictionSourceException error) { logger.LogWarning(error, "Prediction scenario inputs unavailable."); return Results.Problem(statusCode: 503, title: "Дані сценарію тимчасово недоступні."); }
        });
    }
}
