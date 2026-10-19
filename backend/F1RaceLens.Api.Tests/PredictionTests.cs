using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using F1RaceLens.Api.Calendar;
using F1RaceLens.Api.Standings;
using F1RaceLens.Api.Predictions;

namespace F1RaceLens.Api.Tests;

public sealed class PredictionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly StandingsEntry[] Drivers = Enumerable.Range(0, 10).Select(i =>
        new StandingsEntry(i + 1, (i + 1).ToString(), $"d{i}", $"Driver {i}", null, [$"Team {i / 2}"], 100 - i * 5, 0)).ToArray();
    private static readonly StandingsEntry[] Teams = Enumerable.Range(0, 5).Select(i =>
        new StandingsEntry(i + 1, (i + 1).ToString(), $"t{i}", $"Team {i}", null, [], 180 - i * 10, 0)).ToArray();
    private static ForecastObservation[] History(bool classified = true) => Enumerable.Range(1, 3).SelectMany(round =>
        Enumerable.Range(0, 10).Select(i => new ForecastObservation(round, false, $"d{i}", $"t{i / 2}", i + 1, classified || i < 9, 0))).ToArray();
    private static CalendarResponse Calendar(bool remaining = true) => new(2026, Now, false, "Jolpica",
        Enumerable.Range(1, remaining ? 4 : 3).Select(round => new RaceSchedule(2026, round, "GP",
            new CircuitInfo("test", "Test", "Test", "Test"), new DateOnly(2026, 1, round <= 3 ? round : 20), null, [])).ToArray());
    private static StandingsResponse Standings(bool drivers = true, int round = 3) => new(2026, round,
        drivers ? "drivers" : "constructors", Now, false, "Jolpica", drivers ? Drivers : Teams);

    [Fact]
    public void FutureRacePredictionsUseSameSimulationPointsWithoutPastRoundsOrSprints()
    {
        var output = PredictionEngine.Simulate(new ForecastInput(Drivers, Teams, History(), 2, 1), 300);
        Assert.NotNull(output.Races);
        Assert.Equal(2, output.Races.Count);
        Assert.All(output.Races, race => {
            Assert.True(race.Round > 3);
            Assert.Equal(100, race.Drivers.Sum(row => row.WinChance), 5);
            Assert.Equal(300, race.Drivers.Sum(row => row.PodiumChance), 5);
            Assert.Equal(101, race.Drivers.Sum(row => row.ExpectedPoints), 5);
        });
    }
    [Fact]
    public void ConservesAwardedPointsAndProbabilityMassAcrossDriverAndTeamForecasts()
    {
        var output = PredictionEngine.Simulate(new ForecastInput(Drivers, Teams, History(), 2, 1), 300);
        Assert.Equal((double)Drivers.Sum(row => row.Points) + 2 * 101 + 36, output.Drivers.Sum(row => row.ExpectedPoints), 5);
        Assert.Equal((double)Teams.Sum(row => row.Points) + 2 * 101 + 36, output.Teams.Sum(row => row.ExpectedPoints), 5);
        Assert.Equal(100, output.Drivers.Sum(row => row.TitleChance), 5);
        Assert.Equal(100, output.Teams.Sum(row => row.TitleChance), 5);
        Assert.Equal(300, output.Drivers.Sum(row => row.Top3Chance), 5);
        foreach (var row in output.Drivers.Concat(output.Teams))
        {
            Assert.InRange(row.ExpectedPoints, (double)row.CurrentPoints, (double)row.CurrentPoints + 2 * 25 + 2 * 8 + 50);
            Assert.True(row.PointsLow <= row.PointsHigh); Assert.True(row.PositionLow <= row.PositionHigh);
        }
    }
    [Fact]
    public void IncludesNonClassifiedOutcomesAndFreezesInactiveDrivers()
    {
        var drivers = Drivers.Append(new StandingsEntry(11, "11", "reserve", "Reserve", null, [], 5, 0)).ToArray();
        var output = PredictionEngine.Simulate(new ForecastInput(drivers, Teams, History(false), 2, 0), 100);
        Assert.Equal((double)Drivers[9].Points, output.Drivers.Single(row => row.Id == "d9").ExpectedPoints);
        var inactive = output.Drivers.Single(row => row.Id == "reserve");
        Assert.False(inactive.Active); Assert.Equal(5, inactive.ExpectedPoints);
    }
    [Fact]
    public void SameSeedIsReproducibleAndCancellationPropagates()
    {
        var input = new ForecastInput(Drivers, Teams, History(), 2, 1);
        Assert.Equal(JsonSerializer.Serialize(PredictionEngine.Simulate(input, 100)), JsonSerializer.Serialize(PredictionEngine.Simulate(input, 100)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PredictionEngine.Simulate(input, token: cancellation.Token));
    }
    [Fact]
    public void CompletedCalendarPreservesOfficialRanksAndPointsWithoutSimulationUncertainty()
    {
        var output = PredictionService.Build(2026, Now, false, Calendar(false), Standings(), Standings(false), History());
        Assert.Equal("completed", output.Status); Assert.Equal(1, output.Simulations);
        foreach (var row in output.Drivers) { Assert.Equal((double)row.CurrentPoints, row.ExpectedPoints); Assert.Equal(row.CurrentPosition, row.PositionLow); Assert.Equal(row.CurrentPosition, row.PositionHigh); }
        Assert.Equal(100, output.Drivers.Single(row => row.Id == "d0").TitleChance);
    }
    [Fact]
    public void RefusesMismatchedSnapshotsAndMissingPastSessionResults()
    {
        Assert.Equal("synchronizing", PredictionService.Build(2026, Now, false, Calendar(), Standings(), Standings(false, 2), History()).Status);
        Assert.Equal("synchronizing", PredictionService.Build(2026, Now, false, Calendar(), Standings(), Standings(false), History().Where(row => row.Round != 2).ToArray()).Status);
        Assert.Equal("synchronizing", PredictionService.Build(2026, Now, false, Calendar(), Standings(), Standings(false), History().Append(new ForecastObservation(4, true, "d0", "t0", 1, true, 8)).ToArray()).Status);
    }
    [Fact]
    public void RequiresThreeRacesAndNeverExtendsRulesToUnknownFutureYears()
    {
        Assert.Equal("insufficient-data", PredictionService.Build(2026, Now, false, Calendar(), Standings(), Standings(false), []).Status);
        Assert.Equal("unsupported-season", PredictionService.Build(2027, Now, false, Calendar(), Standings(), Standings(false), History()).Status);
    }
    [Fact]
    public async Task SourcePaginatesWholeSeasonIncludingSplitRacesAndPreservesNonClassification()
    {
        var calls = 0;
        var source = new PredictionSource(new Factory(request => {
            var offset = calls++;
            Assert.Contains($"offset={offset}", request.RequestUri!.Query);
            return Payload(offset, 2, offset == 0 ? "d0" : "d1", offset == 0 ? "1" : "R");
        }));
        var rows = await source.FetchAsync(2026, false, CancellationToken.None);
        Assert.Equal(2, calls); Assert.True(rows[0].Classified); Assert.False(rows[1].Classified);
    }
    [Theory]
    [InlineData("wrong-season")]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    public async Task SourceRejectsInvalidSnapshots(string mode)
    {
        var calls = 0;
        var source = new PredictionSource(new Factory(_ => {
            var offset = calls++;
            var json = JsonNode.Parse(Payload(offset, mode == "wrong-season" ? 1 : 2, "d0", "1"))!;
            if (mode == "wrong-season") json["MRData"]!["RaceTable"]!["season"] = "2025";
            if (mode == "truncated") json["MRData"]!["RaceTable"]!["Races"] = new JsonArray();
            return json.ToJsonString();
        }));
        await Assert.ThrowsAsync<PredictionSourceException>(() => source.FetchAsync(2026, false, CancellationToken.None));
    }
    private static string Payload(int offset, int total, string id, string positionText) => new JsonObject {
        ["MRData"] = new JsonObject { ["total"] = total.ToString(), ["offset"] = offset.ToString(),
            ["RaceTable"] = new JsonObject { ["season"] = "2026", ["Races"] = new JsonArray(new JsonObject {
                ["season"] = "2026", ["round"] = "3", ["Results"] = new JsonArray(new JsonObject {
                    ["position"] = (offset + 1).ToString(), ["positionText"] = positionText, ["points"] = "0", ["status"] = "Retired",
                    ["Driver"] = new JsonObject { ["driverId"] = id, ["givenName"] = "Test", ["familyName"] = "Driver" },
                    ["Constructor"] = new JsonObject { ["constructorId"] = "t0", ["name"] = "Team" }
                })
            }) }
        }
    }.ToJsonString();
    private sealed class Factory(Func<HttpRequestMessage, string> respond) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(new Handler(respond)) { BaseAddress = new Uri("https://api.jolpi.ca/ergast/f1/") }; }
    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") }); }
}
