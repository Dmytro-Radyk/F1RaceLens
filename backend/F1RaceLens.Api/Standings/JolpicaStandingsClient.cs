using System.Globalization;
using System.Text.Json;

namespace F1RaceLens.Api.Standings;

public sealed class JolpicaStandingsClient(IHttpClientFactory clients)
{
    public async Task<SeasonStandings> FetchAsync(string season, string kind, CancellationToken cancellationToken, int? snapshotRound = null)
    {
        var (path, field) = kind switch
        {
            "drivers" => ("driverStandings", "DriverStandings"),
            "constructors" => ("constructorStandings", "ConstructorStandings"),
            _ => throw new ArgumentException("Unknown standings kind.", nameof(kind))
        };
        try
        {
            using var client = clients.CreateClient("Jolpica");
            var rows = new List<StandingsEntry>();
            int? expectedTotal = null;
            int? resultSeason = null;
            int? resultRound = null;
            while (true)
            {
                var prefix = snapshotRound is null ? season : $"{season}/{snapshotRound}";
                using var response = await client.GetAsync($"{prefix}/{path}.json?limit=100&offset={rows.Count}", cancellationToken);
                response.EnsureSuccessStatusCode();
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var data = document.RootElement.GetProperty("MRData");
                var total = Number(data, "total");
                if (total > 1000 || Number(data, "offset") != rows.Count || (expectedTotal is not null && expectedTotal != total))
                    throw new FormatException("Invalid standings pagination.");
                expectedTotal = total;
                var table = data.GetProperty("StandingsTable");
                var year = Number(table, "season");
                var round = OptionalNumber(table, "round");
                if (snapshotRound is not null && round != snapshotRound) throw new FormatException("Unexpected snapshot round.");
                if (year < 1950 || (season != "current" && year.ToString(CultureInfo.InvariantCulture) != season)
                    || (resultSeason is not null && (year != resultSeason || round != resultRound)))
                    throw new FormatException("Unexpected standings season or round.");
                resultSeason = year;
                resultRound = round;
                var lists = table.GetProperty("StandingsLists");
                if (lists.GetArrayLength() == 0 && total == 0) return new SeasonStandings(year, round, []);
                if (lists.GetArrayLength() != 1 || round is null or < 1)
                    throw new FormatException("Invalid standings list.");
                var list = lists[0];
                if (Number(list, "season") != year || Number(list, "round") != round)
                    throw new FormatException("Inconsistent standings snapshot.");
                var page = list.GetProperty(field).EnumerateArray().Select(entry => Parse(entry, kind)).ToArray();
                rows.AddRange(page);
                if (rows.Count > total || rows.Select(row => row.Id).Distinct().Count() != rows.Count)
                    throw new FormatException("Duplicated or excessive standings entries.");
                if (rows.Count == total)
                    return new SeasonStandings(year, round, rows.OrderBy(row => row.Position ?? int.MaxValue).ToArray());
                if (page.Length == 0) throw new FormatException("Truncated standings.");
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            throw new StandingsSourceException("Standings source returned an invalid response.", error);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new StandingsSourceException("Standings source timed out.", error);
        }
    }

    private static StandingsEntry Parse(JsonElement entry, string kind)
    {
        var driver = kind == "drivers";
        var participant = entry.GetProperty(driver ? "Driver" : "Constructor");
        var position = OptionalNumber(entry, "position");
        if (position is < 1) throw new FormatException("Invalid standings position.");
        var teams = driver ? entry.GetProperty("Constructors").EnumerateArray()
            .Select(team => Required(team, "name")).Distinct().ToArray() : [];
        return new StandingsEntry(position, Required(entry, "positionText"),
            Required(participant, driver ? "driverId" : "constructorId"),
            driver ? $"{Required(participant, "givenName")} {Required(participant, "familyName")}" : Required(participant, "name"),
            driver ? Text(participant, "code") : null, teams,
            decimal.Parse(Required(entry, "points"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
            Number(entry, "wins"));
    }
    private static string? Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        var text = property.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
    private static string Required(JsonElement value, string name) => Text(value, name) ?? throw new FormatException($"Missing {name}.");
    private static int Number(JsonElement value, string name) => int.Parse(Required(value, name), NumberStyles.None, CultureInfo.InvariantCulture);
    private static int? OptionalNumber(JsonElement value, string name) => Text(value, name) is { } text
        ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) : null;
}
