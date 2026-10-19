using System.Text.Json;
using F1RaceLens.Api.Calendar;
using F1RaceLens.Api.Standings;
using F1RaceLens.Api.Predictions;

if (args.Length > 0 && File.Exists(args[0]))
{
    using var existing = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
    if (existing.RootElement.GetProperty("model").GetString() != PredictionEngine.Version)
        throw new ArgumentException("Use a new report filename: an archived model version must not be overwritten.");
}
var factory = new SourceFactory();
var results = new PredictionSource(factory);
var standings = new JolpicaStandingsClient(factory);
var calendar = await new JolpicaCalendarClient(factory).FetchAsync("2025", CancellationToken.None);
var history = (await results.FetchAsync(2025, false, CancellationToken.None)).Concat(await results.FetchAsync(2025, true, CancellationToken.None)).ToArray();
var prior = (await results.FetchAsync(2024, false, CancellationToken.None)).Concat(await results.FetchAsync(2023, false, CancellationToken.None)).ToArray();
var finalDrivers = await standings.FetchAsync("2025", "drivers", CancellationToken.None);
var finalTeams = await standings.FetchAsync("2025", "constructors", CancellationToken.None);
var report = new List<object>();
foreach (var round in new[] { 8, 12, 18 })
{
    var drivers = await standings.FetchAsync("2025", "drivers", CancellationToken.None, round);
    var teams = await standings.FetchAsync("2025", "constructors", CancellationToken.None, round);
    // Future results are loaded only for evaluation and never passed into the engine.
    var known = history.Where(row => row.Round <= round).ToArray();
    var remaining = calendar.Races.Where(race => race.Round > round).ToArray();
    var output = PredictionEngine.Simulate(new ForecastInput(drivers.Entries, teams.Entries, known,
        remaining.Length, remaining.Count(race => race.Sessions.Any(session => session.Kind == "Sprint")),
        PredictionService.FutureEvents(new CalendarResponse(2025, DateTimeOffset.UtcNow, false, "Jolpica", calendar.Races), round), prior));
    object Evaluate(IReadOnlyList<ForecastRow> predictions, IReadOnlyList<StandingsEntry> snapshot, IReadOnlyList<StandingsEntry> actual)
    {
        var pairs = predictions.Select((prediction, index) => (Prediction: prediction, PredictedRank: index + 1,
            Actual: actual.SingleOrDefault(row => row.Id == prediction.Id))).Where(pair => pair.Actual?.Position is not null).ToArray();
        return new {
            evaluatedParticipants = pairs.Length,
            finalParticipants = actual.Count,
            rankMae = pairs.Average(pair => Math.Abs(pair.PredictedRank - pair.Actual!.Position!.Value)),
            baselineRankMae = pairs.Average(pair => Math.Abs(pair.Prediction.CurrentPosition - pair.Actual!.Position!.Value)),
            pointsMae = pairs.Average(pair => Math.Abs(pair.Prediction.ExpectedPoints - (double)pair.Actual!.Points)),
            baselinePointsMae = pairs.Average(pair => Math.Abs((double)pair.Prediction.CurrentPoints * calendar.Races.Count / round - (double)pair.Actual!.Points)),
            positionIntervalCoverage = pairs.Count(pair => pair.Actual!.Position >= pair.Prediction.PositionLow && pair.Actual.Position <= pair.Prediction.PositionHigh) / (double)pairs.Length,
            actualChampion = actual.First(row => row.Position == 1).Id,
            modelFavourite = predictions.OrderByDescending(row => row.TitleChance).First().Id,
            championChance = predictions.SingleOrDefault(row => row.Id == actual.First(entry => entry.Position == 1).Id)?.TitleChance,
        };
    }
    report.Add(new { year = 2025, throughRound = round, drivers = Evaluate(output.Drivers, drivers.Entries, finalDrivers.Entries), teams = Evaluate(output.Teams, teams.Entries, finalTeams.Entries) });
}
var json = JsonSerializer.Serialize(new { model = PredictionEngine.Version, simulations = 4000, seed = 260019,
    note = "Three retrospective snapshots of 2025 with 2023/2024 circuit history; weather unknown (no retrospective observed weather), no future roster changes or upgrades supplied. Overlapping samples, no calibration. Baseline: current rank and points per completed round.", snapshots = report }, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 0) await File.WriteAllTextAsync(args[0], json);
Console.WriteLine(json);

sealed class SourceFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.jolpi.ca/ergast/f1/"), Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("F1RaceLens/0.1.0");
        return client;
    }
}
