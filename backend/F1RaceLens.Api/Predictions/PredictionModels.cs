using F1RaceLens.Api.Standings;

namespace F1RaceLens.Api.Predictions;

public sealed record ForecastObservation(int Round, bool Sprint, string DriverId, string TeamId,
    int Position, bool Classified, decimal Points, int Season = 0, string CircuitId = "", int? Grid = null);
public sealed record ForecastRow(string Id, string Name, int CurrentPosition, decimal CurrentPoints,
    bool Active, double ExpectedPoints, double PointsLow, double PointsHigh,
    double AveragePosition, int PositionLow, int PositionHigh, double TitleChance, double Top3Chance);
public sealed record RaceForecastRow(string Id, string Name, double ExpectedPoints, double AveragePosition,
    double WinChance, double PodiumChance, double NonClassificationChance);
public sealed record RaceForecast(int Round, string Name, DateOnly Date, IReadOnlyList<RaceForecastRow> Drivers);
public sealed record ForecastOutput(IReadOnlyList<ForecastRow> Drivers, IReadOnlyList<ForecastRow> Teams,
    IReadOnlyList<RaceForecast>? Races = null);
public sealed record PredictionResponse(int Season, DateTimeOffset UpdatedAt, bool IsStale, string Source,
    string Status, string? Message, int ThroughRound, int RemainingRaces, int RemainingSprints,
    int Simulations, string ModelVersion, IReadOnlyList<ForecastRow> Drivers, IReadOnlyList<ForecastRow> Teams,
    PredictionFactors? Factors = null, IReadOnlyList<RaceForecast>? Races = null)
{
    public DateTimeOffset? GeneratedAt { get; init; }
    public Guid? RunId { get; init; }
    public DateTimeOffset? RecordedAt { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public string? InputsJson { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<ForecastEvent>? Schedule { get; init; }
}
public sealed record ForecastInput(IReadOnlyList<StandingsEntry> Drivers, IReadOnlyList<StandingsEntry> Teams,
    IReadOnlyList<ForecastObservation> History, int RemainingRaces, int RemainingSprints,
    IReadOnlyList<ForecastEvent>? Events = null, IReadOnlyList<ForecastObservation>? PriorHistory = null,
    ForecastScenario? Scenario = null);
public sealed record ForecastEvent(int Round, bool Sprint, string CircuitId, string Name, DateOnly Date,
    DateTimeOffset? StartsAt = null, double? Latitude = null, double? Longitude = null, ForecastWeather? Weather = null);
public sealed record ForecastWeather(double? RainProbability, double? Temperature, double? Wind,
    string Status, DateTimeOffset? UpdatedAt = null);
public sealed record DriverReplacement(string OutgoingId, string IncomingId, int FromRound);
public sealed record ForecastScenario(string Weather = "auto", string? DevelopmentTeamId = null,
    double DevelopmentPositions = 0, DriverReplacement? Replacement = null);
public sealed record CircuitFactor(int Round, bool Sprint, string CircuitId, string Name, DateOnly Date,
    int HistoricalRaces, double? AverageGridChange, double? NonClassificationRate,
    double? RainProbability, double? Temperature, double? Wind, string WeatherStatus, DateTimeOffset? WeatherUpdatedAt = null);
public sealed record PredictionFactors(IReadOnlyList<CircuitFactor> Events, IReadOnlyList<int> HistoricalSeasons,
    bool IsScenario, ForecastScenario Settings, IReadOnlyList<string>? RosterIds = null);
public sealed class PredictionSourceException(string message, Exception? inner = null) : Exception(message, inner);
