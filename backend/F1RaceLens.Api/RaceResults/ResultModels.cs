namespace F1RaceLens.Api.RaceResults;

public sealed record DriverResult(
    int Position, string? PositionText, string DriverId, string DriverName, string? DriverCode,
    string? Team, int? Grid, int? Laps, string? Time, string? Status, decimal? Points,
    string? Q1, string? Q2, string? Q3);

public sealed record SessionResultsResponse(
    int Season, int Round, string Kind, DateTimeOffset UpdatedAt, bool IsStale,
    string Source, IReadOnlyList<DriverResult> Entries);

public sealed class ResultsSourceException(string message, Exception? inner = null) : Exception(message, inner);
