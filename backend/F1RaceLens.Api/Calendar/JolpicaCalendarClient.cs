using System.Globalization;
using System.Text.Json;

namespace F1RaceLens.Api.Calendar;

public sealed class JolpicaCalendarClient(IHttpClientFactory clientFactory)
{
    private static readonly IReadOnlyDictionary<string, string> SessionNames = new Dictionary<string, string>
    {
        ["FirstPractice"] = "Практика 1",
        ["SecondPractice"] = "Практика 2",
        ["ThirdPractice"] = "Практика 3",
        ["Qualifying"] = "Кваліфікація",
        ["SprintQualifying"] = "Спринтерська кваліфікація",
        ["SprintShootout"] = "Спринтерська кваліфікація",
        ["Sprint"] = "Спринт"
    };

    public async Task<SeasonSchedule> FetchAsync(string season, CancellationToken cancellationToken)
    {
        try
        {
            using var client = clientFactory.CreateClient("Jolpica");
            using var response = await client.GetAsync($"{season}.json?limit=100", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Parse(document.RootElement, season);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CalendarSourceException("Calendar source timed out.", error);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException
            or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            throw new CalendarSourceException("Calendar source returned unavailable or invalid data.", error);
        }
    }

    private static SeasonSchedule Parse(JsonElement root, string requestedSeason)
    {
        var metadata = root.GetProperty("MRData");
        var table = metadata.GetProperty("RaceTable");
        var season = PositiveInteger(table, "season");
        if (requestedSeason != "current" && season.ToString(CultureInfo.InvariantCulture) != requestedSeason)
            throw new FormatException("Unexpected season.");

        var races = table.GetProperty("Races").EnumerateArray().Select(race => ParseRace(race, season)).ToArray();
        if (PositiveInteger(metadata, "total", allowZero: true) != races.Length)
            throw new FormatException("Incomplete calendar response.");
        if (races.Select(race => race.Round).Distinct().Count() != races.Length)
            throw new FormatException("Duplicate rounds.");

        return new SeasonSchedule(season, races.OrderBy(race => race.Date).ThenBy(race => race.Round).ToArray());
    }

    private static RaceSchedule ParseRace(JsonElement race, int season)
    {
        if (PositiveInteger(race, "season") != season) throw new FormatException("Race season mismatch.");
        var circuit = race.GetProperty("Circuit");
        var location = circuit.GetProperty("Location");
        var date = ReadDate(race);
        var startsAt = ReadTime(race, date);
        var sessions = new List<SessionSchedule>();

        foreach (var property in race.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object || !property.Value.TryGetProperty("date", out _)) continue;
            var sessionDate = ReadDate(property.Value);
            var name = SessionNames.GetValueOrDefault(property.Name, property.Name);
            sessions.Add(new SessionSchedule(property.Name, name, sessionDate, ReadTime(property.Value, sessionDate)));
        }
        sessions.Add(new SessionSchedule("Race", "Гонка", date, startsAt));

        return new RaceSchedule(
            season,
            PositiveInteger(race, "round"),
            RequiredText(race, "raceName"),
            new CircuitInfo(RequiredText(circuit, "circuitId"), RequiredText(circuit, "circuitName"),
                RequiredText(location, "locality"), RequiredText(location, "country"),
                Coordinate(location, "lat", 90), Coordinate(location, "long", 180)),
            date,
            startsAt,
            sessions.OrderBy(session => session.Date).ThenBy(session => session.StartsAt ?? DateTimeOffset.MaxValue).ToArray());
    }

    private static double? Coordinate(JsonElement location, string name, double maximum)
    {
        if (!location.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (!double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var coordinate)
            || !double.IsFinite(coordinate) || Math.Abs(coordinate) > maximum) throw new FormatException("Invalid circuit coordinate.");
        return coordinate;
    }

    private static string RequiredText(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value) ? value : throw new FormatException($"Missing {property}.");
    }

    private static int PositiveInteger(JsonElement element, string property, bool allowZero = false)
    {
        if (!int.TryParse(RequiredText(element, property), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < (allowZero ? 0 : 1)) throw new FormatException($"Invalid {property}.");
        return number;
    }

    private static DateOnly ReadDate(JsonElement element) =>
        DateOnly.ParseExact(RequiredText(element, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ReadTime(JsonElement element, DateOnly date)
    {
        if (!element.TryGetProperty("time", out var time) || time.ValueKind == JsonValueKind.Null
            || string.IsNullOrWhiteSpace(time.GetString())) return null;
        return DateTimeOffset.Parse($"{date:yyyy-MM-dd}T{time.GetString()}", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
