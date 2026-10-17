namespace F1RaceLens.Api.Standings;

public sealed record StandingsEntry(int? Position, string PositionText, string Id, string Name,
    string? Code, IReadOnlyList<string> Teams, decimal Points, int Wins);
public sealed record SeasonStandings(int Season, int? Round, IReadOnlyList<StandingsEntry> Entries);
public sealed record StandingsResponse(int Season, int? Round, string Kind, DateTimeOffset UpdatedAt,
    bool IsStale, string Source, IReadOnlyList<StandingsEntry> Entries);
public sealed class StandingsSourceException(string message, Exception? inner = null) : Exception(message, inner);
