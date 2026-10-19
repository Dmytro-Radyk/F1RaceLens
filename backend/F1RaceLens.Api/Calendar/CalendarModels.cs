namespace F1RaceLens.Api.Calendar;

public sealed record CircuitInfo(string Id, string Name, string Locality, string Country,
    double? Latitude = null, double? Longitude = null);

public sealed record SessionSchedule(string Kind, string Name, DateOnly Date, DateTimeOffset? StartsAt);

public sealed record RaceSchedule(
    int Season,
    int Round,
    string Name,
    CircuitInfo Circuit,
    DateOnly Date,
    DateTimeOffset? StartsAt,
    IReadOnlyList<SessionSchedule> Sessions);

public sealed record SeasonSchedule(int Season, IReadOnlyList<RaceSchedule> Races);

public sealed record CalendarResponse(
    int Season,
    DateTimeOffset UpdatedAt,
    bool IsStale,
    string Source,
    IReadOnlyList<RaceSchedule> Races);

public sealed record RaceResponse(
    int Season,
    DateTimeOffset UpdatedAt,
    bool IsStale,
    string Source,
    RaceSchedule Race);

public sealed class CalendarSourceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
