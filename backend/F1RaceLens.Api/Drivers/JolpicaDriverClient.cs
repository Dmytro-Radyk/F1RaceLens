using System.Globalization;
using System.Text.Json;
using F1RaceLens.Api.RaceResults;

namespace F1RaceLens.Api.Drivers;

public sealed class JolpicaDriverClient(IHttpClientFactory clients)
{
    public async Task<DriverSeasonData?> FetchAsync(int season, string driverId, CancellationToken cancellationToken)
    {
        try
        {
            using var client = clients.CreateClient("Jolpica");
            using var details = await GetAsync(client, $"drivers/{driverId}.json?limit=1", cancellationToken);
            var data = details.RootElement.GetProperty("MRData");
            var drivers = data.GetProperty("DriverTable").GetProperty("Drivers");
            if (Number(data, "total") == 0 && drivers.GetArrayLength() == 0) return null;
            if (Number(data, "total") != 1 || drivers.GetArrayLength() != 1) throw new FormatException("Invalid driver count.");
            var driver = drivers[0];
            if (Required(driver, "driverId") != driverId) throw new FormatException("Unexpected driver.");
            var birth = Text(driver, "dateOfBirth");
            var info = new DriverInfo(driverId, $"{Required(driver, "givenName")} {Required(driver, "familyName")}",
                Text(driver, "code"), birth is null ? null : DateOnly.ParseExact(birth, "yyyy-MM-dd", CultureInfo.InvariantCulture), Text(driver, "nationality"));
            var races = new List<DriverRace>();
            int? expectedTotal = null;
            while (true)
            {
                using var page = await GetAsync(client, $"{season}/drivers/{driverId}/results.json?limit=100&offset={races.Count}", cancellationToken);
                var root = page.RootElement.GetProperty("MRData");
                var total = Number(root, "total");
                if (total > 1000 || Number(root, "offset") != races.Count || (expectedTotal is not null && expectedTotal != total))
                    throw new FormatException("Invalid driver results pagination.");
                expectedTotal = total;
                var table = root.GetProperty("RaceTable");
                if (Number(table, "season") != season || Required(table, "driverId") != driverId)
                    throw new FormatException("Unexpected results filter.");
                var countBefore = races.Count;
                foreach (var race in table.GetProperty("Races").EnumerateArray())
                {
                    var round = Number(race, "round");
                    if (Number(race, "season") != season || round is < 1 or > 100) throw new FormatException("Unexpected race.");
                    foreach (var entry in race.GetProperty("Results").EnumerateArray())
                    {
                        var result = JolpicaResultsClient.Parse(entry);
                        if (result.DriverId != driverId) throw new FormatException("Unexpected result driver.");
                        races.Add(new DriverRace(round, Required(race, "raceName"),
                            DateOnly.ParseExact(Required(race, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                            Required(race.GetProperty("Circuit"), "circuitName"), result));
                    }
                }
                if (races.Count > total) throw new FormatException("Excessive driver results.");
                if (races.Count == total) return new DriverSeasonData(info, races.OrderBy(race => race.Round).ThenBy(race => race.Result.Position).ToArray());
                if (races.Count == countBefore) throw new FormatException("Truncated driver results.");
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            throw new DriverSourceException("Driver source returned an invalid response.", error);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DriverSourceException("Driver source timed out.", error);
        }
    }
    private static async Task<JsonDocument> GetAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var item)
        && item.ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(item.GetString()) ? item.GetString() : null;
    private static string Required(JsonElement value, string name) => Text(value, name) ?? throw new FormatException($"Missing {name}.");
    private static int Number(JsonElement value, string name) => int.Parse(Required(value, name), NumberStyles.None, CultureInfo.InvariantCulture);
}
