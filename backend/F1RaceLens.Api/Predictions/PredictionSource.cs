using System.Globalization;
using System.Text.Json;
using F1RaceLens.Api.RaceResults;

namespace F1RaceLens.Api.Predictions;

public sealed class PredictionSource(IHttpClientFactory clients)
{
    public async Task<IReadOnlyList<ForecastObservation>> FetchAsync(int year, bool sprint, CancellationToken token)
    {
        try
        {
            using var client = clients.CreateClient("Jolpica");
            var rows = new List<ForecastObservation>();
            int? expected = null;
            while (true)
            {
                using var response = await client.GetAsync($"{year}/{(sprint ? "sprint" : "results")}.json?limit=100&offset={rows.Count}", token);
                response.EnsureSuccessStatusCode();
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
                var data = document.RootElement.GetProperty("MRData");
                var total = Number(data, "total");
                if (total is < 0 or > 2000 || Number(data, "offset") != rows.Count || (expected is not null && total != expected))
                    throw new FormatException("Invalid prediction pagination.");
                expected = total;
                var table = data.GetProperty("RaceTable");
                if (Number(table, "season") != year) throw new FormatException("Unexpected prediction season.");
                var countBefore = rows.Count;
                foreach (var race in table.GetProperty("Races").EnumerateArray())
                {
                    var round = Number(race, "round");
                    if (round is < 1 or > 40 || Number(race, "season") != year) throw new FormatException("Invalid prediction round.");
                    foreach (var entry in race.GetProperty(sprint ? "SprintResults" : "Results").EnumerateArray())
                    {
                        var parsed = JolpicaResultsClient.Parse(entry);
                        var team = entry.GetProperty("Constructor").GetProperty("constructorId").GetString();
                        if (string.IsNullOrWhiteSpace(team) || parsed.Points is null || parsed.Position > 40)
                            throw new FormatException("Missing prediction entry fields.");
                        rows.Add(new ForecastObservation(round, sprint, parsed.DriverId, team, parsed.Position,
                            int.TryParse(parsed.PositionText, NumberStyles.None, CultureInfo.InvariantCulture, out var classified) && classified > 0,
                            parsed.Points.Value, year,
                            race.TryGetProperty("Circuit", out var circuit) && circuit.TryGetProperty("circuitId", out var circuitId)
                                ? circuitId.GetString() ?? "" : "", parsed.Grid));
                    }
                }
                if (rows.Count > total || rows.Select(row => (row.Round, row.DriverId)).Distinct().Count() != rows.Count)
                    throw new FormatException("Duplicated prediction entries.");
                if (rows.Count == total) return rows;
                if (rows.Count == countBefore) throw new FormatException("Truncated prediction entries.");
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { throw new PredictionSourceException("Invalid prediction source response.", error); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new PredictionSourceException("Prediction source timed out.", error); }
    }
    private static int Number(JsonElement value, string name) => value.GetProperty(name).GetString() is { } text
        ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) : throw new FormatException($"Missing {name}.");
}
