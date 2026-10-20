using System.Security.Cryptography;
using System.Text.Json;

namespace F1RaceLens.Api.Predictions.Integration;

// Fixed administrator-configured path; no user-supplied files, commands or training in HTTP requests.
public sealed class LocalMlForecast(IHostEnvironment environment, IConfiguration configuration, TimeProvider clock)
{
    public async Task<PredictionResponse> ReadAsync(CancellationToken token)
    {
        var root = configuration["Predictions:MlRoot"] ?? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../../ml"));
        var text = await File.ReadAllTextAsync(Path.Combine(root, "reports/season-forecast-lightgbm-v4.json"), token);
        using var document = JsonDocument.Parse(text); var data = document.RootElement;
        if (data.GetProperty("model").GetString() != "lightgbm-race-v4") throw new InvalidDataException("Unexpected ML model.");
        var modelPath = Path.Combine(root, "models/lightgbm-v4");
        foreach (var name in new[] { "pace.txt", "pace.schema.json", "nonclassification.txt", "nonclassification.schema.json" })
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(modelPath, name), token);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != data.GetProperty("artifact_sha256").GetProperty(name).GetString())
                throw new InvalidDataException("ML weights do not match the forecast.");
        }
        var input = data.GetProperty("input_snapshot");
        var metadataText = await File.ReadAllTextAsync(Path.Combine(modelPath, "metadata.json"), token);
        if (!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(metadataText),
            System.Text.Json.Nodes.JsonNode.Parse(input.GetProperty("model_metadata").GetRawText()))
            || input.GetProperty("model_metadata").GetProperty("engine").GetString() != "lightgbm")
            throw new InvalidDataException("ML metadata differs from the forecast.");
        var snapshot = input.GetProperty("standings_calendar");
        var season = data.GetProperty("season").GetInt32(); var through = data.GetProperty("through_round").GetInt32();
        var generated = data.GetProperty("generated").GetDateTimeOffset().ToUniversalTime();
        var updated = data.GetProperty("source_updated").GetDateTimeOffset().ToUniversalTime();
        var now = clock.GetUtcNow();
        if (season != now.Year || generated > now || updated > generated || through != snapshot.GetProperty("through").GetInt32()
            || season != snapshot.GetProperty("season").GetInt32()
            || input.GetProperty("history").EnumerateArray().Any(x => x.GetProperty("round").GetInt32() > through))
            throw new InvalidDataException("ML input cutoff is inconsistent.");
        var schedule = snapshot.GetProperty("calendar").EnumerateArray().Where(x => int.Parse(x.GetProperty("round").GetString()!, System.Globalization.CultureInfo.InvariantCulture) > through)
            .Select(x => new ForecastEvent(int.Parse(x.GetProperty("round").GetString()!, System.Globalization.CultureInfo.InvariantCulture), false,
                x.GetProperty("Circuit").GetProperty("circuitId").GetString()!, x.GetProperty("raceName").GetString()!,
                DateOnly.Parse(x.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                x.TryGetProperty("time", out var time) ? DateTimeOffset.Parse(x.GetProperty("date").GetString() + "T" + time.GetString(), System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime() : null)).ToArray();
        var races = data.GetProperty("races").EnumerateArray().Select(x => new RaceForecast(x.GetProperty("round").GetInt32(),
            x.GetProperty("name").GetString()!, DateOnly.Parse(x.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            x.GetProperty("drivers").EnumerateArray().Select(r => new RaceForecastRow(r.GetProperty("driver").GetString()!, r.GetProperty("name").GetString()!,
                Number(r,"expected_points"), Number(r,"average_position"), Number(r,"win_chance"), Number(r,"podium_chance"), Number(r,"risk") * 100)).ToArray())).ToArray();
        if (!races.Select(x => x.Round).Order().SequenceEqual(schedule.Select(x => x.Round).Order())) throw new InvalidDataException("ML calendar mismatch.");
        foreach (var race in races) Journal.ForecastJournal.ValidateRace(race);
        var obsolete = schedule.Any(x => x.StartsAt <= now || x.StartsAt is null && x.Date <= DateOnly.FromDateTime(now.UtcDateTime));
        return new(season, updated, updated.AddHours(24) < now, "Jolpica · LightGBM", obsolete ? "synchronizing" : data.GetProperty("status").GetString()!,
            obsolete ? "Потрібен новий розрахунок ML після завершеного етапу." : null,
            through, data.GetProperty("remaining_races").GetInt32(), data.GetProperty("remaining_sprints").GetInt32(),
            data.GetProperty("simulations").GetInt32(), "lightgbm-race-v4", Rows(data.GetProperty("drivers")), Rows(data.GetProperty("teams")),
            Races: obsolete ? [] : races) { GeneratedAt = generated, InputsJson = JsonSerializer.Serialize(new {
                input_snapshot = input, artifact_sha256 = data.GetProperty("artifact_sha256"), seed = data.GetProperty("seed"),
                forecast_version = data.GetProperty("forecast_version")
            }), Schedule = schedule };
    }

    private static double Number(JsonElement row, string key) => row.GetProperty(key).GetDouble();
    private static ForecastRow[] Rows(JsonElement rows) => rows.EnumerateArray().Select(r => new ForecastRow(
        r.GetProperty("id").GetString()!, r.GetProperty("name").GetString()!, r.GetProperty("position").GetInt32(),
        r.GetProperty("points").GetDecimal(), r.GetProperty("active").GetBoolean(), Number(r,"expected_points"), Number(r,"points_low"), Number(r,"points_high"),
        Number(r,"average_position"), r.GetProperty("position_low").GetInt32(), r.GetProperty("position_high").GetInt32(), Number(r,"title_chance"), Number(r,"top3_chance"))).ToArray();
}
