using System.Globalization;
using System.Text.Json;

namespace F1RaceLens.Api.RaceResults;

public sealed class JolpicaResultsClient(IHttpClientFactory clients)
{
    public async Task<IReadOnlyList<DriverResult>> FetchAsync(int season, int round, string kind, CancellationToken cancellationToken)
    {
        var (path, field) = kind switch
        {
            "race" => ("results", "Results"),
            "qualifying" => ("qualifying", "QualifyingResults"),
            "sprint" => ("sprint", "SprintResults"),
            _ => throw new ArgumentException("Unknown session kind.", nameof(kind))
        };
        try
        {
            using var client = clients.CreateClient("Jolpica");
            using var response = await client.GetAsync($"{season}/{round}/{path}.json?limit=100", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var data = document.RootElement.GetProperty("MRData");
            var table = data.GetProperty("RaceTable");
            if (Number(table, "season") != season || Number(table, "round") != round)
                throw new FormatException("Unexpected race.");
            var races = table.GetProperty("Races");
            if (races.GetArrayLength() > 1) throw new FormatException("Unexpected race count.");
            if (races.GetArrayLength() == 0)
            {
                if (Number(data, "total") != 0) throw new FormatException("Truncated results.");
                return [];
            }
            var race = races[0];
            if (Number(race, "season") != season || Number(race, "round") != round)
                throw new FormatException("Unexpected result season or round.");
            var entries = race.GetProperty(field).EnumerateArray().Select(Parse).ToArray();
            if (entries.Length != Number(data, "total")) throw new FormatException("Truncated results.");
            return entries.OrderBy(entry => entry.Position).ToArray();
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            throw new ResultsSourceException("Results source returned an invalid response.", error);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ResultsSourceException("Results source timed out.", error);
        }
    }

    internal static DriverResult Parse(JsonElement entry)
    {
        var driver = entry.GetProperty("Driver");
        var position = Number(entry, "position");
        if (position < 1) throw new FormatException("Invalid position.");
        var team = entry.TryGetProperty("Constructor", out var constructor) ? Text(constructor, "name") : null;
        var time = entry.TryGetProperty("Time", out var timing) ? Text(timing, "time") : null;
        var points = Text(entry, "points");
        decimal? score = points is null ? null : decimal.Parse(points, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return new DriverResult(position, Text(entry, "positionText"), Required(driver, "driverId"),
            $"{Required(driver, "givenName")} {Required(driver, "familyName")}", Text(driver, "code"),
            team, OptionalNumber(entry, "grid"), OptionalNumber(entry, "laps"), time, Text(entry, "status"), score,
            Text(entry, "Q1"), Text(entry, "Q2"), Text(entry, "Q3"));
    }

    private static string? Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        var text = property.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
    private static string Required(JsonElement value, string name) => !string.IsNullOrWhiteSpace(Text(value, name))
        ? Text(value, name)! : throw new FormatException($"Missing {name}.");
    private static int Number(JsonElement value, string name) => int.Parse(Required(value, name), NumberStyles.None, CultureInfo.InvariantCulture);
    private static int? OptionalNumber(JsonElement value, string name) => Text(value, name) is { } text
        ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) : null;
}
