using F1RaceLens.Api.RaceResults;
using F1RaceLens.Api.Standings;

namespace F1RaceLens.Api.Drivers;

public sealed record DriverInfo(string Id, string Name, string? Code, DateOnly? DateOfBirth, string? Nationality);
public sealed record DriverRace(int Round, string Name, DateOnly Date, string Circuit, DriverResult Result);
public sealed record DriverSeasonData(DriverInfo Driver, IReadOnlyList<DriverRace> Races);
public sealed record DriverProfileResponse(int Season, DateTimeOffset UpdatedAt, bool IsStale, string Source,
    DriverInfo Driver, int? StandingsRound, StandingsEntry? Standing, IReadOnlyList<DriverRace> Races);
public sealed class DriverSourceException(string message, Exception? inner = null) : Exception(message, inner);
