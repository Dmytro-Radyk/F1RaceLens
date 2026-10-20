using System.Globalization;
using System.Text.Json;
using F1RaceLens.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1RaceLens.Api.Storage;

// Projections retain the source document and ordinal. Only a complete validated API response publishes its manifest.
public sealed class SourceProjector(RaceLensDbContext db)
{
    public async Task ImportAsync(SourceDocument document, CancellationToken token)
    {
        using var json = JsonDocument.Parse(document.Payload);
        var data = json.RootElement.GetProperty("MRData");
        if (data.TryGetProperty("DriverTable", out var driverTable))
            foreach (var driver in Array(driverTable, "Drivers")) await Driver(driver, token);
        if (data.TryGetProperty("ConstructorTable", out var constructorTable))
            foreach (var team in Array(constructorTable, "Constructors")) await Constructor(team, token);
        if (data.TryGetProperty("RaceTable", out var races))
        {
            var ordinal = 0;
            foreach (var race in Array(races, "Races"))
            {
                var year = Int(race, "season")!.Value;
                var round = Int(race, "round")!.Value;
                await Season(year, token);
                var circuit = race.GetProperty("Circuit");
                var circuitId = Text(circuit, "circuitId")!;
                await Upsert(db.Circuits, [circuitId], row => {
                    row.Id = circuitId; row.Name = Text(circuit, "circuitName")!;
                    if (circuit.TryGetProperty("Location", out var location)) {
                        row.Country = Text(location, "country"); row.Locality = Text(location, "locality");
                        row.Latitude = Double(location, "lat"); row.Longitude = Double(location, "long");
                    }
                }, token);
                await Upsert(db.Races, [year, round], row => {
                    row.Season = year; row.Round = round; row.CircuitId = circuitId;
                    row.Name = Text(race, "raceName")!; row.Date = Date(race, "date")!.Value;
                    row.StartsAt = StartsAt(race) ?? row.StartsAt;
                }, token);
                await Upsert(db.Sessions, [year, round, "race"], row => {
                    row.Season = year; row.Round = round; row.Kind = "race";
                    row.Date = Date(race, "date")!.Value; row.StartsAt = StartsAt(race) ?? row.StartsAt;
                }, token);
                foreach (var (field, kind) in new[] { ("FirstPractice", "practice1"), ("SecondPractice", "practice2"),
                    ("ThirdPractice", "practice3"), ("Qualifying", "qualifying"), ("Sprint", "sprint"),
                    ("SprintQualifying", "sprint-qualifying"), ("SprintShootout", "sprint-qualifying") })
                    if (race.TryGetProperty(field, out var session))
                        await Upsert(db.Sessions, [year, round, kind], row => {
                            row.Season = year; row.Round = round; row.Kind = kind;
                            row.Date = Date(session, "date")!.Value; row.StartsAt = StartsAt(session);
                        }, token);
                foreach (var (field, kind) in new[] { ("Results", "race"), ("QualifyingResults", "qualifying"), ("SprintResults", "sprint") })
                    foreach (var result in Array(race, field))
                    {
                        var driverId = await Driver(result.GetProperty("Driver"), token);
                        string? teamId = null;
                        if (result.TryGetProperty("Constructor", out var team)) {
                            teamId = await Constructor(team, token);
                            await Participation(year, driverId, teamId, token);
                        }
                        db.Results.Add(new ResultRecord {
                            DocumentId = document.Id, Ordinal = ordinal++, Season = year, Round = round,
                            Kind = kind, DriverId = driverId, ConstructorId = teamId,
                            CarNumber = Text(result, "number"), Position = Int(result, "position"),
                            PositionText = Text(result, "positionText"), Grid = Int(result, "grid"),
                            Status = Text(result, "status"), Points = Decimal(result, "points"), Payload = result.GetRawText()
                        });
                    }
            }
        }
        if (data.TryGetProperty("StandingsTable", out var standings))
        {
            var ordinal = 0;
            foreach (var list in Array(standings, "StandingsLists"))
            {
                var year = Int(list, "season")!.Value;
                await Season(year, token);
                foreach (var (field, kind) in new[] { ("DriverStandings", "drivers"), ("ConstructorStandings", "constructors") })
                    foreach (var entry in Array(list, field))
                    {
                        var id = kind == "drivers" ? await Driver(entry.GetProperty("Driver"), token)
                            : await Constructor(entry.GetProperty("Constructor"), token);
                        if (kind == "drivers")
                            foreach (var team in Array(entry, "Constructors"))
                                await Participation(year, id, await Constructor(team, token), token);
                        db.Standings.Add(new StandingRecord {
                            DocumentId = document.Id, Ordinal = ordinal++, Season = year, Round = Int(list, "round"), Kind = kind,
                            ParticipantId = id, PositionText = Text(entry, "positionText") ?? Text(entry, "position"),
                            Points = Decimal(entry, "points")!.Value, Wins = Int(entry, "wins")!.Value,
                            Payload = entry.GetRawText()
                        });
                    }
            }
        }
    }
    private async Task<string> Driver(JsonElement value, CancellationToken token)
    {
        var id = Text(value, "driverId")!;
        await Upsert(db.Drivers, [id], row => {
            row.Id = id; row.Name = $"{Text(value, "givenName")} {Text(value, "familyName")}";
            row.Code = Text(value, "code") ?? row.Code;
            row.BirthDate = Date(value, "dateOfBirth") ?? row.BirthDate;
            row.Nationality = Text(value, "nationality") ?? row.Nationality;
        }, token);
        return id;
    }
    private async Task<string> Constructor(JsonElement value, CancellationToken token)
    {
        var id = Text(value, "constructorId")!;
        await Upsert(db.Constructors, [id], row => { row.Id = id; row.Name = Text(value, "name")!; }, token);
        return id;
    }
    private Task Season(int year, CancellationToken token) => Upsert(db.Seasons, [year], row => row.Year = year, token);
    private Task Participation(int year, string driver, string team, CancellationToken token) =>
        Upsert(db.Participations, [year, driver, team], row => { row.Season = year; row.DriverId = driver; row.ConstructorId = team; }, token);
    private static async Task Upsert<T>(DbSet<T> set, object[] key, Action<T> update, CancellationToken token) where T : class, new()
    {
        var row = await set.FindAsync(key, token);
        if (row is null) { row = new T(); update(row); set.Add(row); }
        else update(row);
    }
    private static IEnumerable<JsonElement> Array(JsonElement value, string name) =>
        value.TryGetProperty(name, out var list) ? list.EnumerateArray() : [];
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? item.GetString() : null;
    private static int? Int(JsonElement value, string name) => Text(value, name) is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : null;
    private static decimal? Decimal(JsonElement value, string name) => Text(value, name) is { } s ? decimal.Parse(s, CultureInfo.InvariantCulture) : null;
    private static double? Double(JsonElement value, string name) => Text(value, name) is { } s ? double.Parse(s, CultureInfo.InvariantCulture) : null;
    private static DateOnly? Date(JsonElement value, string name) => Text(value, name) is { } s ? DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    private static DateTimeOffset? StartsAt(JsonElement value) => Text(value, "time") is { } time && Text(value, "date") is { } date
        ? DateTimeOffset.Parse(date + "T" + time, CultureInfo.InvariantCulture).ToUniversalTime() : null;
}
