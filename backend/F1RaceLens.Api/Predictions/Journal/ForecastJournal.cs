using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using F1RaceLens.Api.RaceResults;
using F1RaceLens.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace F1RaceLens.Api.Predictions.Journal;

public sealed class ForecastJournal(IDbContextFactory<RaceLensDbContext> factory, TimeProvider clock)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<PredictionResponse?> ReadLatestScriptAsync(int year, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(token);
        var run = await db.ForecastRuns.AsNoTracking().Where(x => x.Engine == "script" && x.Season == year
            && x.GeneratedAt > now.AddMinutes(-15) && x.GeneratedAt <= now && x.SourceUpdatedAt > now.AddHours(-24))
            .OrderByDescending(x => x.ThroughRound).ThenByDescending(x => x.SourceUpdatedAt)
            .ThenByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(token);
        if (run is null) return null;
        if (Hash(run.Output) != run.OutputHash || Hash(run.Inputs) != run.InputHash)
            throw new InvalidDataException("Published forecast integrity check failed.");
        var result = JsonSerializer.Deserialize<PredictionResponse>(run.Output, Json)!;
        return result with { RunId = run.Id, RecordedAt = run.RecordedAt, InputsJson = run.Inputs };
    }

    public async Task<PredictionResponse> SaveAsync(string engine, PredictionResponse forecast, CancellationToken token)
    {
        if (forecast.Status != "ready" || forecast.Factors?.IsScenario == true || forecast.InputsJson is null
            || forecast.GeneratedAt is null || forecast.IsStale) return forecast;
        var now = clock.GetUtcNow();
        if (forecast.GeneratedAt > now || forecast.UpdatedAt > forecast.GeneratedAt)
            throw new InvalidDataException("Invalid forecast chronology.");
        var output = JsonSerializer.Serialize(forecast with { RunId = null, RecordedAt = null }, Json);
        var inputHash = Hash(forecast.InputsJson); var outputHash = Hash(output);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(749138202)", token);
        var run = await db.ForecastRuns.SingleOrDefaultAsync(x => x.Engine == engine && x.OutputHash == outputHash && x.InputHash == inputHash, token);
        if (run is null)
        {
            run = new ForecastRun { Id = Guid.NewGuid(), Engine = engine, Version = forecast.ModelVersion,
                Season = forecast.Season, ThroughRound = forecast.ThroughRound, GeneratedAt = forecast.GeneratedAt.Value,
                RecordedAt = now, SourceUpdatedAt = forecast.UpdatedAt, Inputs = forecast.InputsJson,
                InputHash = inputHash, Output = output, OutputHash = outputHash };
            db.ForecastRuns.Add(run);
        }
        foreach (var race in forecast.Races ?? [])
        {
            var schedule = forecast.Schedule?.SingleOrDefault(x => !x.Sprint && x.Round == race.Round);
            if (schedule?.StartsAt is not { } start || start <= now || race.Round <= forecast.ThroughRound) continue;
            ValidateRace(race);
            if (await db.ForecastSeals.AnyAsync(x => x.Engine == engine && x.Version == forecast.ModelVersion
                && x.Season == forecast.Season && x.Round == race.Round && x.ThroughRound == forecast.ThroughRound, token)) continue;
            db.ForecastSeals.Add(new ForecastSeal { Id = Guid.NewGuid(), RunId = run.Id, Engine = engine,
                Version = forecast.ModelVersion, Season = forecast.Season, Round = race.Round,
                ThroughRound = forecast.ThroughRound, Name = race.Name, StartsAt = start, FrozenAt = now });
        }
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return forecast with { RunId = run.Id, RecordedAt = run.RecordedAt };
    }

    public static void ValidateRace(RaceForecast race)
    {
        if (race.Drivers.Count < 2 || race.Drivers.Select(x => x.Id).Distinct().Count() != race.Drivers.Count
            || race.Drivers.Any(x => string.IsNullOrEmpty(x.Id) || !double.IsFinite(x.WinChance) || x.WinChance is < 0 or > 100
                || !double.IsFinite(x.ExpectedPoints) || x.ExpectedPoints < 0
                || !double.IsFinite(x.AveragePosition) || x.AveragePosition < 1 || x.AveragePosition > race.Drivers.Count
                || !double.IsFinite(x.PodiumChance) || x.PodiumChance is < 0 or > 100 || x.PodiumChance < x.WinChance
                || !double.IsFinite(x.NonClassificationChance) || x.NonClassificationChance is < 0 or > 100)
            || Math.Abs(race.Drivers.Sum(x => x.WinChance) - 100) > .1)
            throw new InvalidDataException("Invalid forecast distribution.");
    }

    public static (double Mae, double Brier, bool Hit)? Score(RaceForecast predicted, IReadOnlyList<DriverResult> actual)
    {
        ValidateRace(predicted);
        if (actual.Count != predicted.Drivers.Count || actual.Select(x => x.DriverId).Distinct().Count() != actual.Count
            || !actual.Select(x => x.DriverId).Order().SequenceEqual(predicted.Drivers.Select(x => x.Id).Order())
            || !actual.Select(x => x.Position).Order().SequenceEqual(Enumerable.Range(1, actual.Count))) return null;
        var positions = actual.ToDictionary(x => x.DriverId, x => x.Position);
        var winner = actual.Single(x => x.Position == 1).DriverId;
        var ordered = predicted.Drivers.OrderByDescending(x => x.ExpectedPoints).ThenBy(x => x.AveragePosition).ThenBy(x => x.Id).ToArray();
        var mae = ordered.Select((x, index) => (double)Math.Abs(index + 1 - positions[x.Id])).Average();
        var brier = predicted.Drivers.Sum(x => Math.Pow(x.WinChance / 100 - (x.Id == winner ? 1 : 0), 2));
        var favourite = predicted.Drivers.OrderByDescending(x => x.WinChance).ThenBy(x => x.Id).First().Id;
        return (mae, brier, favourite == winner);
    }

    public async Task<AccuracyResponse> AssessAsync(ResultsService results, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var now = clock.GetUtcNow();
        var seals = await db.ForecastSeals.AsNoTracking().Where(x => x.Season == now.Year)
            .OrderBy(x => x.Round).ThenBy(x => x.Engine).ThenBy(x => x.ThroughRound).ToListAsync(token);
        var actuals = new Dictionary<int, SessionResultsResponse?>();
        var rows = new List<AccuracyRow>();
        foreach (var seal in seals)
        {
            var row = new AccuracyRow(seal.Engine, seal.Version, seal.Season, seal.ThroughRound, seal.Round, seal.Name, seal.FrozenAt, "pending");
            if (now <= seal.StartsAt) { rows.Add(row); continue; }
            if (!actuals.TryGetValue(seal.Round, out var actual))
            {
                try { actual = await results.GetAsync(seal.Season, seal.Round, "race", token); }
                catch (ResultsSourceException) { actual = null; }
                actuals[seal.Round] = actual;
            }
            if (actual is null || actual.IsStale || actual.Entries.Count == 0) { rows.Add(row with { Status = "awaiting-results" }); continue; }
            var run = await db.ForecastRuns.AsNoTracking().SingleAsync(x => x.Id == seal.RunId, token);
            if (Hash(run.Output) != run.OutputHash || Hash(run.Inputs) != run.InputHash)
                throw new InvalidDataException("Forecast journal integrity check failed.");
            var forecast = JsonSerializer.Deserialize<PredictionResponse>(run.Output, Json)!;
            var race = forecast.Races!.Single(x => x.Round == seal.Round);
            var metrics = Score(race, actual.Entries);
            if (metrics is null) { rows.Add(row with { Status = "roster-mismatch" }); continue; }
            var payload = JsonSerializer.Serialize(actual.Entries, Json); var hash = Hash(payload);
            // Append result revisions; never rewrite a sealed prediction or an older score.
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(749138202)", token);
            if (!await db.ForecastScores.AnyAsync(x => x.SealId == seal.Id && x.ActualHash == hash, token))
            {
                db.ForecastScores.Add(new ForecastScore { Id = Guid.NewGuid(), SealId = seal.Id, ActualHash = hash,
                    Actual = payload, ResultUpdatedAt = actual.UpdatedAt, EvaluatedAt = now, RankMae = metrics.Value.Mae,
                    WinnerBrier = metrics.Value.Brier, WinnerHit = metrics.Value.Hit });
                await db.SaveChangesAsync(token);
            }
            await transaction.CommitAsync(token);
            rows.Add(row with { Status = "scored", RankMae = metrics.Value.Mae, WinnerBrier = metrics.Value.Brier, WinnerHit = metrics.Value.Hit });
        }
        return new(now, rows);
    }
}
