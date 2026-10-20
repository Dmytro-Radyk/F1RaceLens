using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace F1RaceLens.Api.Teams;

// A participation list is deliberately separate from championship standings.
public sealed record Participant(string Id, string Name);
public sealed record ParticipantsResponse(int Season, DateTimeOffset UpdatedAt, bool IsStale,
    string Source, IReadOnlyList<Participant> Entries);
public sealed class ParticipantsSourceException(string message, Exception inner) : Exception(message, inner);

public sealed partial class HistoricalTeams(IHttpClientFactory clients, TimeProvider clock)
{
    private readonly ConcurrentDictionary<(int, string?), ParticipantsResponse> cache = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<ParticipantsResponse> GetAsync(int year, string? teamId, CancellationToken token)
    {
        if (year is < 1950 or > 1957 || (teamId is not null && !ValidId().IsMatch(teamId)))
            throw new ArgumentException("Invalid participation filter.");
        var key = (year, teamId);
        if (cache.TryGetValue(key, out var saved) && saved.UpdatedAt.AddMinutes(15) > clock.GetUtcNow()) return saved;
        await refreshLock.WaitAsync(token);
        try
        {
            if (cache.TryGetValue(key, out saved) && saved.UpdatedAt.AddMinutes(15) > clock.GetUtcNow()) return saved;
            try
            {
                var entries = await FetchAsync(year, teamId, token);
                var result = new ParticipantsResponse(year, clock.GetUtcNow(), false, "Jolpica", entries);
                cache[key] = result;
                return result;
            }
            catch (ParticipantsSourceException) when (saved is not null && saved.UpdatedAt.AddHours(24) > clock.GetUtcNow())
            {
                return saved with { IsStale = true };
            }
        }
        finally { refreshLock.Release(); }
    }

    private async Task<IReadOnlyList<Participant>> FetchAsync(int year, string? teamId, CancellationToken token)
    {
        try
        {
            using var client = clients.CreateClient("Jolpica");
            var entries = new List<Participant>();
            int? expectedTotal = null;
            var isTeamList = teamId is null;
            var path = isTeamList ? $"{year}/constructors" : $"{year}/constructors/{teamId}/drivers";
            while (true)
            {
                using var response = await client.GetAsync($"{path}.json?limit=100&offset={entries.Count}", token);
                response.EnsureSuccessStatusCode();
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
                var data = document.RootElement.GetProperty("MRData");
                var total = Number(data, "total");
                if (total is < 0 or > 1000 || Number(data, "offset") != entries.Count || (expectedTotal is not null && total != expectedTotal))
                    throw new FormatException("Invalid participation pagination.");
                expectedTotal = total;
                var table = data.GetProperty(isTeamList ? "ConstructorTable" : "DriverTable");
                if (Number(table, "season") != year || (!isTeamList && Required(table, "constructorId") != teamId))
                    throw new FormatException("Unexpected participation filter.");
                var page = table.GetProperty(isTeamList ? "Constructors" : "Drivers").EnumerateArray().Select(row =>
                {
                    var id = Required(row, isTeamList ? "constructorId" : "driverId");
                    if (!ValidId().IsMatch(id)) throw new FormatException("Invalid participant identifier.");
                    return new Participant(id, isTeamList ? Required(row, "name") : $"{Required(row, "givenName")} {Required(row, "familyName")}");
                }).ToArray();
                entries.AddRange(page);
                if (entries.Count > total || entries.Select(entry => entry.Id).Distinct().Count() != entries.Count)
                    throw new FormatException("Duplicate or excessive participants.");
                if (entries.Count == total) return entries.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
                if (page.Length == 0) throw new FormatException("Truncated participants.");
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { throw new ParticipantsSourceException("Invalid participation source response.", error); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new ParticipantsSourceException("Participation source timed out.", error); }
    }
    private static string Required(JsonElement value, string name) => value.GetProperty(name).GetString() is { } text && !string.IsNullOrWhiteSpace(text)
        ? text : throw new FormatException($"Missing {name}.");
    private static int Number(JsonElement value, string name) => int.Parse(Required(value, name), NumberStyles.None, CultureInfo.InvariantCulture);
    [GeneratedRegex(@"\A[a-z0-9_-]{1,80}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ValidId();

    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/seasons/{year:int}/participants/teams", (int year, HistoricalTeams source, HttpContext context, ILogger<HistoricalTeams> logger) =>
            Respond(year, null, source, context, logger));
        app.MapGet("/api/seasons/{year:int}/participants/teams/{teamId}/drivers", (int year, string teamId, HistoricalTeams source, HttpContext context, ILogger<HistoricalTeams> logger) =>
            Respond(year, teamId, source, context, logger));
    }
    private static async Task<IResult> Respond(int year, string? teamId, HistoricalTeams source, HttpContext context, ILogger logger)
    {
        if (year is < 1950 or > 1957 || (teamId is not null && !ValidId().IsMatch(teamId)))
            return Results.Problem(statusCode: 400, title: "Некоректний історичний сезон або команда.");
        try { return Results.Ok(await source.GetAsync(year, teamId, context.RequestAborted)); }
        catch (ParticipantsSourceException error)
        {
            logger.LogWarning(error, "Historical participants unavailable for {Year}/{Team}.", year, teamId);
            context.Response.Headers.RetryAfter = "60";
            return Results.Problem(statusCode: 503, title: "Учасники сезону тимчасово недоступні.");
        }
    }
}
