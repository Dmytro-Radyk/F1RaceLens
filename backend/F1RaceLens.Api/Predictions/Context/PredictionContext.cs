namespace F1RaceLens.Api.Predictions;

public sealed record PreparedDriver(string DriverId, string TeamId, ForecastObservation[] Pool, double Affinity, double TeamCorrection);
public sealed record PreparedSession(ForecastEvent Event, PreparedDriver[] Drivers, double NoiseScale, double ExtraFailure);

public static class PredictionContext
{
    public static IReadOnlyList<ForecastEvent> Events(ForecastInput input) => input.Events ??
        Enumerable.Range(0, input.RemainingRaces + input.RemainingSprints).Select(index => new ForecastEvent(
            input.History.Where(row => !row.Sprint).Max(row => row.Round) + 1 + index,
            index >= input.RemainingRaces, "", "Гран-прі", default)).ToArray();

    public static IReadOnlyList<PreparedSession> Prepare(ForecastInput input)
    {
        var current = input.History.Where(row => !row.Sprint).ToArray();
        var last = current.Max(row => row.Round);
        var roster = current.Where(row => row.Round == last).ToArray();
        var prior = (input.PriorHistory ?? []).Where(row => !row.Sprint).ToArray();
        var all = prior.Concat(current).ToArray();
        var globalChange = GridChange(prior);
        var globalFailures = prior.Length == 0 ? 0 : prior.Count(row => !row.Classified) / (double)prior.Length;
        return Events(input).Select(item => {
            var track = prior.Where(row => item.CircuitId.Length > 0 && row.CircuitId == item.CircuitId).ToArray();
            var trackChange = GridChange(track);
            var trackFailures = track.Length == 0 ? globalFailures : track.Count(row => !row.Classified) / (double)track.Length;
            var shrink = track.Length / (track.Length + 40d);
            var noise = globalChange > 0 && trackChange is not null ? 1 + (Math.Clamp(trackChange.Value / globalChange.Value, .6, 1.6) - 1) * shrink : 1;
            var participants = roster.Select(row => input.Scenario?.Replacement is { } replacement && item.Round >= replacement.FromRound && row.DriverId == replacement.OutgoingId
                ? row with { DriverId = replacement.IncomingId } : row).Select(row => {
                var sameTeam = current.Where(entry => entry.DriverId == row.DriverId && entry.TeamId == row.TeamId).ToArray();
                var personal = sameTeam.Length >= 3 ? sameTeam : current.Where(entry => entry.DriverId == row.DriverId).ToArray();
                var sprint = input.History.Where(entry => entry.Sprint && entry.DriverId == row.DriverId && entry.TeamId == row.TeamId).ToArray();
                var pool = item.Sprint && sprint.Length >= 3 ? sprint : personal;
                if (pool.Length == 0) pool = current.Where(entry => entry.TeamId == row.TeamId).ToArray();
                var correction = pool.Length == 0 ? 0 : TeamMean(current, row.TeamId) - pool.Select(entry => TeamMean(current, entry.TeamId)).Average();
                return new PreparedDriver(row.DriverId, row.TeamId, pool.OrderBy(entry => entry.Round).ToArray(), Affinity(all, row.DriverId, item.CircuitId), Math.Clamp(correction * .5, -3, 3));
            }).ToArray();
            return new PreparedSession(item, participants, noise, Math.Clamp((trackFailures - globalFailures) * shrink * .3, 0, .05));
        }).ToArray();
    }
    public static PredictionFactors Describe(ForecastInput input)
    {
        var prior = input.PriorHistory ?? [];
        var scenario = input.Scenario ?? new();
        return new(Events(input).Select(item => {
            var track = prior.Where(row => !row.Sprint && item.CircuitId.Length > 0 && row.CircuitId == item.CircuitId).ToArray();
            var weather = scenario.Weather is "wet" or "dry" ? new ForecastWeather(scenario.Weather == "wet" ? 1 : 0, null, null, "scenario") : item.Weather;
            return new CircuitFactor(item.Round, item.Sprint, item.CircuitId, item.Name, item.Date,
                track.Select(row => (row.Season, row.Round)).Distinct().Count(), GridChange(track),
                track.Length == 0 ? null : track.Count(row => !row.Classified) / (double)track.Length,
                weather?.RainProbability, weather?.Temperature, weather?.Wind, weather?.Status ?? "unknown", weather?.UpdatedAt);
        }).ToArray(), prior.Select(row => row.Season).Where(year => year > 0).Distinct().Order().ToArray(),
            input.Scenario is not null && input.Scenario != new ForecastScenario(), scenario,
            input.History.Where(row => !row.Sprint && row.Round == input.History.Where(item => !item.Sprint).Max(item => item.Round)).Select(row => row.DriverId).ToArray());
    }
    public static void ValidateScenario(ForecastInput input, ForecastScenario scenario)
    {
        if (scenario.Weather is not ("auto" or "dry" or "wet") || !double.IsFinite(scenario.DevelopmentPositions) || Math.Abs(scenario.DevelopmentPositions) > 3
            || (scenario.DevelopmentTeamId is not null && input.Teams.All(team => team.Id != scenario.DevelopmentTeamId))
            || (scenario.DevelopmentPositions != 0 && scenario.DevelopmentTeamId is null)) throw new ArgumentException("Некоректні параметри погоди або розвитку команди.");
        if (scenario.Replacement is not { } change) return;
        var last = input.History.Where(row => !row.Sprint).Max(row => row.Round);
        var roster = input.History.Where(row => !row.Sprint && row.Round == last).ToArray();
        if (roster.All(row => row.DriverId != change.OutgoingId) || roster.Any(row => row.DriverId == change.IncomingId)
            || input.Drivers.All(driver => driver.Id != change.IncomingId) || !Events(input).Any(item => item.Round == change.FromRound))
            throw new ArgumentException("Для заміни виберіть активного пілота, іншого пілота зі заліку поза поточним складом і майбутній етап.");
    }
    private static double? GridChange(IEnumerable<ForecastObservation> rows)
    {
        var eligible = rows.Where(row => row.Classified && row.Grid > 0).ToArray();
        return eligible.Length == 0 ? null : eligible.Average(row => Math.Abs(row.Grid!.Value - row.Position));
    }
    private static double TeamMean(ForecastObservation[] current, string team)
    {
        var rows = current.Where(row => row.TeamId == team && row.Classified).OrderByDescending(row => row.Round).Take(6).ToArray();
        return rows.Length == 0 ? (current.Length == 0 ? 10 : current.Average(row => row.Position)) : rows.Average(row => row.Position);
    }
    private static double Affinity(ForecastObservation[] all, string driver, string circuit)
    {
        if (circuit.Length == 0) return 0;
        var eligible = all.Where(row => row.DriverId == driver && row.Classified).ToArray();
        var track = eligible.Where(row => row.CircuitId == circuit).ToArray();
        if (track.Length == 0) return 0;
        var differences = track.Select(row => row.Position - eligible.Where(other => other.Season == row.Season && other.TeamId == row.TeamId).Average(other => other.Position)).ToArray();
        return Math.Clamp(differences.Average() * track.Length / (track.Length + 5d), -1.5, 1.5);
    }
}
