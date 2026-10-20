using F1RaceLens.Api.Standings;

namespace F1RaceLens.Api.Predictions;

public static class PredictionEngine
{
    public const string Version = "context-bootstrap-v2";
    public const int DefaultSeed = 260019;
    private static readonly int[] RacePoints = [25, 18, 15, 12, 10, 8, 6, 4, 2, 1];
    private static readonly int[] SprintPoints = [8, 7, 6, 5, 4, 3, 2, 1];

    public static ForecastOutput Simulate(ForecastInput input, int simulations = 4000, int seed = DefaultSeed, CancellationToken token = default)
    {
        if (simulations < 1 || input.RemainingRaces < 0 || input.RemainingSprints < 0 || input.History.Count == 0)
            throw new ArgumentException("Invalid simulation input.");
        if (input.Scenario is not null) PredictionContext.ValidateScenario(input, input.Scenario);
        var drivers = input.Drivers.ToArray();
        var teams = input.Teams.ToArray();
        var raceHistory = input.History.Where(row => !row.Sprint).ToArray();
        var lastRound = raceHistory.Max(row => row.Round);
        var season = raceHistory.Select(row => row.Season).Where(year => year > 0).DefaultIfEmpty().Max();
        if ((input.PriorHistory ?? []).Any(row => season > 0 && row.Season >= season))
            throw new ArgumentException("Circuit history must precede the forecast season.");
        var roster = raceHistory.Where(row => row.Round == lastRound).ToArray();
        var driverIndex = drivers.Select((row, index) => (row.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        var teamIndex = teams.Select((row, index) => (row.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        if (roster.Length < 2 || roster.Select(row => row.DriverId).Distinct().Count() != roster.Length
            || roster.Any(row => !driverIndex.ContainsKey(row.DriverId) || !teamIndex.ContainsKey(row.TeamId)))
            throw new ArgumentException("Inconsistent simulation roster.");
        var sessions = PredictionContext.Prepare(input);
        if (sessions.Any(item => item.Event.Round <= lastRound)) throw new ArgumentException("Cannot simulate an already observed round.");
        if (sessions.Count(item => !item.Event.Sprint) != input.RemainingRaces || sessions.Count(item => item.Event.Sprint) != input.RemainingSprints)
            throw new ArgumentException("Session counts disagree with remaining calendar.");
        var activeIds = sessions.Count > 0 ? sessions.SelectMany(item => item.Drivers).Select(row => row.DriverId).ToHashSet() : roster.Select(row => row.DriverId).ToHashSet();
        var activeTeams = roster.Select(row => row.TeamId).ToHashSet();
        var driverPoints = drivers.Select(_ => new double[simulations]).ToArray();
        var teamPoints = teams.Select(_ => new double[simulations]).ToArray();
        var driverRanks = drivers.Select(_ => new int[simulations]).ToArray();
        var teamRanks = teams.Select(_ => new int[simulations]).ToArray();
        var random = new Random(seed);
        var driverCounts = drivers.Select(row => Counts(raceHistory.Where(item => item.DriverId == row.Id))).ToArray();
        var teamCounts = teams.Select(row => Counts(raceHistory.Where(item => item.TeamId == row.Id))).ToArray();
        var raceTotals = sessions.Where(item => !item.Event.Sprint).ToDictionary(item => item.Event.Round,
            _ => new double[drivers.Length, 5]);
        for (var simulation = 0; simulation < simulations; simulation++)
        {
            token.ThrowIfCancellationRequested();
            var dp = drivers.Select(row => (double)row.Points).ToArray();
            var tp = teams.Select(row => (double)row.Points).ToArray();
            var dc = driverCounts.Select(count => (int[])count.Clone()).ToArray();
            var tc = teamCounts.Select(count => (int[])count.Clone()).ToArray();
            foreach (var session in sessions)
            {
                var sprint = session.Event.Sprint;
                var rain = input.Scenario?.Weather == "wet" ? 1 : input.Scenario?.Weather == "dry" ? 0 : session.Event.Weather?.RainProbability;
                var wet = rain is > 0 && random.NextDouble() < rain;
                var noise = session.NoiseScale * (wet ? 1.6 : 1);
                var failure = session.ExtraFailure + (wet ? .04 : 0);
                var teamNoise = activeTeams.ToDictionary(id => id, _ => Normal(random) * 1.5 * noise);
                var field = session.Drivers.Select(row => {
                    var pool = row.Pool;
                    // A 50/50 mixture of all-season and last-five empirical outcomes.
                    var sample = pool[random.NextDouble() < .5 ? random.Next(pool.Length) : random.Next(Math.Max(0, pool.Length - 5), pool.Length)];
                    var classified = sample.Classified && (failure <= 0 || random.NextDouble() >= failure);
                    var finalRound = sessions.Max(item => item.Event.Round);
                    var progress = (session.Event.Round - lastRound) / (double)Math.Max(1, finalRound - lastRound);
                    var development = row.TeamId == input.Scenario?.DevelopmentTeamId ? input.Scenario.DevelopmentPositions * progress : 0;
                    var limitedSampleNoise = pool.Length < 3 ? 1.4 : 1;
                    return (Row: row, Classified: classified,
                        Score: sample.Position + row.Affinity + row.TeamCorrection - development + teamNoise[row.TeamId] + Normal(random) * noise * limitedSampleNoise);
                }).OrderByDescending(item => item.Classified).ThenBy(item => item.Score).ToArray();
                var scoring = sprint ? SprintPoints : RacePoints;
                for (var position = 0; position < field.Length; position++)
                {
                    var item = field[position];
                    var di = driverIndex[item.Row.DriverId]; var ti = teamIndex[item.Row.TeamId];
                    var points = item.Classified && position < scoring.Length ? scoring[position] : 0;
                    dp[di] += points; tp[ti] += points;
                    if (!sprint)
                    {
                        var totals = raceTotals[session.Event.Round];
                        totals[di, 0] += points; totals[di, 1] += position + 1;
                        if (item.Classified && position == 0) totals[di, 2]++;
                        if (item.Classified && position < 3) totals[di, 3]++;
                        if (!item.Classified) totals[di, 4]++;
                    }
                    if (!sprint && item.Classified) { dc[di][position]++; tc[ti][position]++; }
                }
            }
            var dr = Rank(dp, dc, drivers, random, input.RemainingRaces + input.RemainingSprints == 0);
            var tr = Rank(tp, tc, teams, random, input.RemainingRaces + input.RemainingSprints == 0);
            for (var i = 0; i < drivers.Length; i++) { driverPoints[i][simulation] = dp[i]; driverRanks[i][simulation] = dr[i]; }
            for (var i = 0; i < teams.Length; i++) { teamPoints[i][simulation] = tp[i]; teamRanks[i][simulation] = tr[i]; }
        }
        var races = sessions.Where(item => !item.Event.Sprint).Select(session => {
            var totals = raceTotals[session.Event.Round];
            var rows = session.Drivers.Select(item => {
                var i = driverIndex[item.DriverId];
                return new RaceForecastRow(item.DriverId, drivers[i].Name, totals[i, 0] / simulations,
                    totals[i, 1] / simulations, 100 * totals[i, 2] / simulations,
                    100 * totals[i, 3] / simulations, 100 * totals[i, 4] / simulations);
            }).OrderByDescending(row => row.ExpectedPoints).ThenBy(row => row.AveragePosition).ToArray();
            return new RaceForecast(session.Event.Round, session.Event.Name, session.Event.Date, rows);
        }).ToArray();
        return new ForecastOutput(Summarise(drivers, driverPoints, driverRanks, activeIds), Summarise(teams, teamPoints, teamRanks, activeTeams), races);
    }
    private static int[] Counts(IEnumerable<ForecastObservation> rows)
    {
        var counts = new int[40];
        foreach (var row in rows.Where(row => row.Classified && row.Position <= 40)) counts[row.Position - 1]++;
        return counts;
    }
    private static int[] Rank(double[] points, int[][] counts, StandingsEntry[] entries, Random random, bool completed)
    {
        if (completed) return entries.Select(row => row.Position!.Value).ToArray();
        var tieNoise = entries.Select(_ => random.NextDouble()).ToArray();
        var order = Enumerable.Range(0, entries.Length).ToArray();
        Array.Sort(order, (a, b) => {
            var comparison = points[b].CompareTo(points[a]);
            for (var position = 0; position < 40 && comparison == 0; position++) comparison = counts[b][position].CompareTo(counts[a][position]);
            return comparison != 0 ? comparison : tieNoise[a].CompareTo(tieNoise[b]);
        });
        var ranks = new int[entries.Length];
        for (var i = 0; i < order.Length; i++) ranks[order[i]] = i + 1;
        return ranks;
    }
    private static IReadOnlyList<ForecastRow> Summarise(StandingsEntry[] entries, double[][] points, int[][] ranks, HashSet<string> active)
        => entries.Select((entry, i) => {
            var sortedPoints = points[i].Order().ToArray(); var sortedRanks = ranks[i].Order().ToArray();
            var lo = (int)Math.Floor((sortedPoints.Length - 1) * .1); var hi = (int)Math.Ceiling((sortedPoints.Length - 1) * .9);
            return new ForecastRow(entry.Id, entry.Name, entry.Position!.Value, entry.Points, active.Contains(entry.Id),
                points[i].Average(), sortedPoints[lo], sortedPoints[hi], ranks[i].Average(), sortedRanks[lo], sortedRanks[hi],
                100d * ranks[i].Count(rank => rank == 1) / ranks[i].Length, 100d * ranks[i].Count(rank => rank <= 3) / ranks[i].Length);
        }).OrderByDescending(row => row.ExpectedPoints).ThenBy(row => row.AveragePosition).ToArray();
    private static double Normal(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
}
