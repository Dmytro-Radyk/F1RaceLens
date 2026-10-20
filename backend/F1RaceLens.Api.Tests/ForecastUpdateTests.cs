using System.Text.Json;
using F1RaceLens.Api.Predictions;
using F1RaceLens.Api.Predictions.Updates;
using F1RaceLens.Api.Predictions.Journal;
using F1RaceLens.Api.Calendar;
using F1RaceLens.Api.Standings;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1RaceLens.Api.Tests;

public sealed partial class StorageTests
{
    [Theory]
    [InlineData("running", "2026-10-19T11:59:00Z", "running")]
    [InlineData("running", "2026-10-19T09:00:00Z", "interrupted")]
    [InlineData("failed", "2026-10-19T11:59:00Z", "failed")]
    [InlineData("succeeded", "2026-10-19T11:59:00Z", "succeeded")]
    [InlineData("password=hidden", "2026-10-19T11:59:00Z", "unavailable")]
    public void Update_status_is_sanitized_and_detects_interrupted_process(string state, string updated, string expected)
    {
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(new { state, updatedAt = updated, stage = "arbitrary command",
            lastSuccessfulAt = "2026-10-18T12:00:00Z", error = "password=hidden", secret = "hidden" }));
        var result = ForecastUpdateStatus.Parse(data.RootElement, Now);
        Assert.Equal(expected, result.State); Assert.Null(result.Stage);
        Assert.DoesNotContain("hidden", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Publication_refuses_different_cutoffs_stale_or_unsynchronized_forecasts()
    {
        var source = TestForecast();
        ForecastUpdateJob.ValidatePair(source, source);
        Assert.Throws<InvalidOperationException>(() => ForecastUpdateJob.ValidatePair(source, source with { ThroughRound = 16 }));
        Assert.Throws<InvalidOperationException>(() => ForecastUpdateJob.ValidatePair(source, source with { IsStale = true }));
        Assert.Throws<InvalidOperationException>(() => ForecastUpdateJob.ValidatePair(source, source with { Status = "synchronizing" }));
        var driver = new ForecastRow("a", "A", 1, 10, true, 20, 15, 25, 1, 1, 2, 50, 100);
        Assert.Throws<InvalidOperationException>(() => ForecastUpdateJob.ValidatePair(source with { Drivers = [driver] },
            source with { Drivers = [driver with { CurrentPoints = 11 }] }));
    }

    [PostgreSqlFact]
    public async Task Running_service_adopts_new_publication_and_scenarios_use_its_exact_inputs_without_network()
    {
        await using var fixture = await DatabaseFixture.Create();
        var clock = new FixedClock(Now);
        var journal = new ForecastJournal(fixture.Factory, clock);
        var factory = new ForecastTestFactory(() => throw new InvalidOperationException("Network must not be called."));
        var calendar = new CalendarResponse(2026, Now, false, "Test", [new(2026,18,"Next GP", new("test","Test","Test","Test"),
            new(2026,10,25), Now.AddDays(6), [new("Race","Race",new(2026,10,25),Now.AddDays(6))])]);
        StandingsResponse Standings(bool teams, decimal points) => new(2026,17,teams ? "constructors" : "drivers",Now,false,"Test",
            [new(1,"1",teams ? "ta" : "a","A",null,[],points,0),new(2,"2",teams ? "tb" : "b","B",null,[],5,0)]);
        PredictionResponse Forecast(decimal points) => TestForecast() with {
            GeneratedAt = clock.Now, UpdatedAt = clock.Now.AddSeconds(-1), InputsJson = JsonSerializer.Serialize(new {
                calendar, drivers = Standings(false, points), teams = Standings(true, points),
                history = new ForecastObservation[] {new(17,false,"a","ta",1,true,25,2026),new(17,false,"b","tb",2,true,18,2026)},
                prior = Array.Empty<ForecastObservation>(), events = TestForecast().Schedule
            }) };
        var first = await journal.SaveAsync("script", Forecast(10), default);
        var service = new PredictionService(new(new(factory),clock,NullLogger<CalendarService>.Instance),
            new(new(factory),clock,NullLogger<StandingsService>.Instance),new(factory),
            new(factory,clock,NullLogger<ForecastWeatherClient>.Instance),clock,NullLogger<PredictionService>.Instance,journal);
        Assert.Equal(first.RunId, (await service.GetAsync(default)).RunId);
        clock.Now = Now.AddMinutes(2);
        var latest = await journal.SaveAsync("script", Forecast(200), default);
        Assert.Equal(latest.RunId, (await service.GetAsync(default)).RunId);
        var scenario = await service.ScenarioAsync(new("dry"), default);
        Assert.Equal(200, scenario.Drivers.Single(x => x.Id == "a").CurrentPoints);
        Assert.True(scenario.Factors!.IsScenario);
    }
}
