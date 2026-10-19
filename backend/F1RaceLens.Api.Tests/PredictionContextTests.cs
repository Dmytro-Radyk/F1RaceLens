using System.Text.Json;
using F1RaceLens.Api.Predictions;
using F1RaceLens.Api.Standings;

namespace F1RaceLens.Api.Tests;

public sealed class PredictionContextTests
{
    private static readonly StandingsEntry[] Drivers = Enumerable.Range(0, 5).Select(i => new StandingsEntry(i + 1, (i + 1).ToString(), $"d{i}", $"Driver {i}", null, [], 10, 0)).ToArray();
    private static readonly StandingsEntry[] Teams = [new(1, "1", "t0", "Team 0", null, [], 20, 0), new(2, "2", "t1", "Team 1", null, [], 20, 0)];
    private static ForecastObservation[] History() => Enumerable.Range(1, 3).SelectMany(round => Enumerable.Range(0, 4).Select(i =>
        new ForecastObservation(round, false, $"d{i}", $"t{i / 2}", i + 1, true, 0, 2026, "other", 4 - i))).ToArray();
    private static ForecastInput Input() => new(Drivers, Teams, History(), 2, 0,
        [new(4, false, "track", "Track", new DateOnly(2026, 10, 25)), new(5, false, "other", "Other", new DateOnly(2026, 11, 1))]);

    [Fact]
    public void UnknownCircuitAndWeatherRemainNeutralRatherThanInventingValues()
    {
        var input = Input(); var session = PredictionContext.Prepare(input)[0];
        Assert.Equal(1, session.NoiseScale); Assert.Equal(0, session.ExtraFailure);
        Assert.All(session.Drivers, driver => Assert.Equal(0, driver.Affinity));
        var factor = PredictionContext.Describe(input).Events[0];
        Assert.Equal(0, factor.HistoricalRaces); Assert.Null(factor.RainProbability); Assert.Null(factor.NonClassificationRate);
    }
    [Fact]
    public void HistoricalCircuitContextHasShrinkageAndDoesNotLeakFutureSeason()
    {
        var prior = Enumerable.Range(1, 2).SelectMany(round => Enumerable.Range(0, 4).Select(i => new ForecastObservation(round, false, $"d{i}", $"t{i / 2}",
            round == 1 ? 4 - i : i + 1, true, 0, 2025, round == 1 ? "track" : "other", i + 1))).ToArray();
        var input = Input() with { PriorHistory = prior };
        var session = PredictionContext.Prepare(input)[0];
        Assert.True(session.Drivers.Single(row => row.DriverId == "d0").Affinity > 0);
        Assert.InRange(session.Drivers[0].Affinity, -1.5, 1.5);
        var factor = PredictionContext.Describe(input).Events[0];
        Assert.Equal(1, factor.HistoricalRaces); Assert.NotNull(factor.AverageGridChange);
        Assert.Throws<ArgumentException>(() => PredictionEngine.Simulate(input with { PriorHistory = prior.Select(row => row with { Season = 2026 }).ToArray() }, 10));
    }
    [Fact]
    public void ReplacementStartsAtRequestedRoundAndDoesNotTransferExistingPoints()
    {
        var input = Input() with { Scenario = new("dry", Replacement: new("d0", "d4", 5)) };
        var sessions = PredictionContext.Prepare(input);
        Assert.Contains(sessions[0].Drivers, row => row.DriverId == "d0");
        Assert.DoesNotContain(sessions[1].Drivers, row => row.DriverId == "d0");
        Assert.Equal("t0", sessions[1].Drivers.Single(row => row.DriverId == "d4").TeamId);
        var output = PredictionEngine.Simulate(input, 100);
        Assert.True(output.Drivers.Single(row => row.Id == "d4").ExpectedPoints > 10);
        Assert.All(output.Drivers, row => Assert.True(row.ExpectedPoints >= (double)row.CurrentPoints));
        var onlyReplacement = input with { Scenario = new("dry", Replacement: new("d0", "d4", 4)) };
        Assert.Equal(10, PredictionEngine.Simulate(onlyReplacement, 100).Drivers.Single(row => row.Id == "d0").ExpectedPoints);
    }
    [Theory]
    [InlineData("d0", "d1", 4)]
    [InlineData("unknown", "d4", 4)]
    [InlineData("d0", "unknown", 4)]
    [InlineData("d0", "d4", 3)]
    public void RejectsDuplicateSeatUnknownDriverAndAlreadyCompletedRound(string outgoing, string incoming, int round)
        => Assert.Throws<ArgumentException>(() => PredictionContext.ValidateScenario(Input(), new(Replacement: new(outgoing, incoming, round))));
    [Fact]
    public void RejectsInvalidDevelopmentAndWeatherScenarios()
    {
        Assert.Throws<ArgumentException>(() => PredictionContext.ValidateScenario(Input(), new("storm")));
        Assert.Throws<ArgumentException>(() => PredictionContext.ValidateScenario(Input(), new(DevelopmentPositions: 1)));
        Assert.Throws<ArgumentException>(() => PredictionContext.ValidateScenario(Input(), new(DevelopmentTeamId: "t0", DevelopmentPositions: double.NaN)));
        Assert.Throws<ArgumentException>(() => PredictionContext.ValidateScenario(Input(), new(DevelopmentTeamId: "unknown", DevelopmentPositions: 1)));
    }
    [Fact]
    public void DevelopmentAndWetStressActuallyChangeTheSimulationAndBaselineIsNotMutated()
    {
        var input = Input();
        var baseline = PredictionEngine.Simulate(input, 300);
        var improved = PredictionEngine.Simulate(input with { Scenario = new("dry", "t1", 3) }, 300);
        Assert.True(improved.Teams.Single(row => row.Id == "t1").ExpectedPoints > baseline.Teams.Single(row => row.Id == "t1").ExpectedPoints);
        var wet = PredictionEngine.Simulate(input with { Scenario = new("wet") }, 300);
        Assert.NotEqual(baseline.Drivers[0].ExpectedPoints, wet.Drivers.Single(row => row.Id == baseline.Drivers[0].Id).ExpectedPoints);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(baseline), System.Text.Json.JsonSerializer.Serialize(PredictionEngine.Simulate(input, 300)));
    }
    [Fact]
    public void WeatherUsesOnlyTheThreeHourSessionWindowAndKeepsUnavailableRainUnknown()
    {
        using var document = JsonDocument.Parse("""{"utc_offset_seconds":0,"hourly":{"time":["2026-10-25T12:00","2026-10-25T13:00","2026-10-25T14:00","2026-10-25T15:00"],"precipitation_probability":[100,10,40,20],"temperature_2m":[25,26,27,28],"wind_speed_10m":[5,6,7,8]}}""");
        var start = new DateTimeOffset(2026, 10, 25, 13, 30, 0, TimeSpan.Zero);
        var result = ForecastWeatherClient.Parse(document.RootElement, start, start.AddDays(-2));
        Assert.Equal(.4, result.RainProbability); Assert.Equal(26, result.Temperature); Assert.Equal(6, result.Wind);
        Assert.Equal("forecast", result.Status);
        var missing = ForecastWeatherClient.Parse(document.RootElement, start.AddDays(30), start.AddDays(-2));
        Assert.Null(missing.RainProbability); Assert.Equal("incomplete", missing.Status);
        using var invalid = JsonDocument.Parse("""{"utc_offset_seconds":3600}""");
        Assert.Equal("invalid", ForecastWeatherClient.Parse(invalid.RootElement, start, start).Status);
    }
    [Fact]
    public void ScenarioMetadataNeverPretendsForcedWeatherIsARealForecast()
    {
        var input = Input() with { Scenario = new("wet") };
        var context = PredictionContext.Describe(input);
        Assert.True(context.IsScenario); Assert.All(context.Events, item => { Assert.Equal("scenario", item.WeatherStatus); Assert.Equal(1, item.RainProbability); Assert.Null(item.WeatherUpdatedAt); });
        Assert.Equal(4, context.RosterIds!.Count);
    }
}
