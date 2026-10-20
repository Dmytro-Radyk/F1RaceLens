namespace F1RaceLens.Api.Predictions.Journal;

public sealed class ForecastRun
{
    public Guid Id { get; set; }
    public string Engine { get; set; } = "";
    public string Version { get; set; } = "";
    public int Season { get; set; }
    public int ThroughRound { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public string InputHash { get; set; } = "";
    public string Inputs { get; set; } = "";
    public string OutputHash { get; set; } = "";
    public string Output { get; set; } = "";
}

// First database observation before the start, separately for every cutoff/version.
public sealed class ForecastSeal
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string Engine { get; set; } = "";
    public string Version { get; set; } = "";
    public int Season { get; set; }
    public int ThroughRound { get; set; }
    public int Round { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset FrozenAt { get; set; }
}

public sealed class ForecastScore
{
    public Guid Id { get; set; }
    public Guid SealId { get; set; }
    public string ActualHash { get; set; } = "";
    public string Actual { get; set; } = "";
    public DateTimeOffset ResultUpdatedAt { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
    public double RankMae { get; set; }
    public double WinnerBrier { get; set; }
    public bool WinnerHit { get; set; }
}

public sealed record AccuracyRow(string Engine, string Version, int Season, int ThroughRound, int Round,
    string Name, DateTimeOffset FrozenAt, string Status, double? RankMae = null,
    double? WinnerBrier = null, bool? WinnerHit = null);
public sealed record AccuracyResponse(DateTimeOffset UpdatedAt, IReadOnlyList<AccuracyRow> Rows);
