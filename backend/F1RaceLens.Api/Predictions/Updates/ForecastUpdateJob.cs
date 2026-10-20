using System.Collections.Concurrent;
using System.Text.Json;
using F1RaceLens.Api.Calendar;
using F1RaceLens.Api.Standings;
using F1RaceLens.Api.RaceResults;
using F1RaceLens.Api.Storage;
using F1RaceLens.Api.Predictions.Journal;
using F1RaceLens.Api.Predictions.Integration;

namespace F1RaceLens.Api.Predictions.Updates;

public sealed class ForecastUpdateJob(IServiceProvider services, TimeProvider clock)
{
    public async Task RefreshDataAsync(CancellationToken token)
    {
        var year = clock.GetUtcNow().Year;
        if (year != 2026) throw new InvalidOperationException("Update points rules for the new season first.");
        var season = year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var calendars = services.GetRequiredService<CalendarService>();
        var standings = services.GetRequiredService<StandingsService>();
        var results = services.GetRequiredService<ResultsService>();
        var store = services.GetRequiredService<SnapshotStore>();
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        try
        {
            async Task<T> Save<T>(string key, Func<Task<T>> fetch)
            {
                var pages = new ConcurrentBag<CapturedSource>();
                accessor.HttpContext = new DefaultHttpContext();
                accessor.HttpContext.Items[SourceCaptureHandler.CaptureKey] = pages;
                var data = await fetch();
                var json = JsonSerializer.Serialize(data, ForecastJournal.Json);
                using var parsed = JsonDocument.Parse(json);
                if (parsed.RootElement.GetProperty("isStale").GetBoolean()) throw new InvalidOperationException("Fresh source data required.");
                await store.SaveAsync(key, json, pages.ToArray(), token);
                return data;
            }
            var prefix = $"/api/seasons/{year}";
            var calendar = await Save(prefix+"/calendar", () => calendars.GetAsync(season, token));
            var drivers = await Save(prefix+"/standings/drivers", () => standings.GetAsync(season, "drivers", token));
            var teams = await Save(prefix+"/standings/constructors", () => standings.GetAsync(season, "constructors", token));
            if (drivers.Round != teams.Round) throw new InvalidOperationException("Standings are still synchronizing.");
            foreach (var race in calendar.Races)
            {
                var detail = new RaceResponse(year, calendar.UpdatedAt, false, calendar.Source, race);
                await store.SaveAsync(prefix+$"/races/{race.Round}", JsonSerializer.Serialize(detail, ForecastJournal.Json), [], token);
                if (race.Round > (drivers.Round ?? 0)) continue;
                foreach (var kind in new[] { "race", "qualifying", "sprint" })
                {
                    if (kind == "sprint" && !race.Sessions.Any(x => x.Kind == "Sprint")) continue;
                    var key = prefix+$"/races/{race.Round}/results/{kind}";
                    // Refresh recent results for revisions; backfill missing older data without downloading the entire season every time.
                    if (race.Round < (drivers.Round ?? 0) - 1 && await store.ReadAsync(key, year, token) is { } saved)
                    {
                        using var payload = JsonDocument.Parse(saved.Payload);
                        if (payload.RootElement.GetProperty("entries").GetArrayLength() > 0) continue;
                    }
                    var response = await Save(key, () => results.GetAsync(year, race.Round, kind, token));
                    if (kind == "race" && response.Entries.Count == 0) throw new InvalidOperationException("Confirmed race results are incomplete.");
                    await Task.Delay(400, token);
                }
            }
        }
        finally { accessor.HttpContext = previous; }
    }

    public async Task PublishAsync(CancellationToken token)
    {
        var script = await services.GetRequiredService<PredictionService>().GetAsync(token, readPublished: false);
        var ml = await services.GetRequiredService<LocalMlForecast>().ReadAsync(token);
        ValidatePair(script, ml);
        var journal = services.GetRequiredService<ForecastJournal>();
        await journal.SaveAsync("script", script, token);
        await journal.SaveAsync("ml", ml, token);
        await journal.AssessAsync(services.GetRequiredService<ResultsService>(), token);
    }

    public static void ValidatePair(PredictionResponse script, PredictionResponse ml)
    {
        static bool SameStandings(IReadOnlyList<ForecastRow> first, IReadOnlyList<ForecastRow> second) => first
            .Select(x => (x.Id, x.CurrentPoints, x.CurrentPosition, x.Active)).OrderBy(x => x.Id)
            .SequenceEqual(second.Select(x => (x.Id, x.CurrentPoints, x.CurrentPosition, x.Active)).OrderBy(x => x.Id));
        if (script.Status is not ("ready" or "completed") || ml.Status != script.Status || script.IsStale || ml.IsStale
            || script.Season != ml.Season || script.ThroughRound != ml.ThroughRound
            || script.RemainingRaces != ml.RemainingRaces || script.RemainingSprints != ml.RemainingSprints
            || !(script.Races ?? []).Select(x => (x.Round, x.Date)).Order().SequenceEqual((ml.Races ?? []).Select(x => (x.Round, x.Date)).Order())
            || !SameStandings(script.Drivers, ml.Drivers) || !SameStandings(script.Teams, ml.Teams))
            throw new InvalidOperationException("Forecasts require matching fresh season, cutoff and calendar. Retry after source synchronization.");
    }
}
