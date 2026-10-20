using F1RaceLens.Api.Predictions;
using F1RaceLens.Api.Predictions.Journal;
using F1RaceLens.Api.RaceResults;
using Microsoft.EntityFrameworkCore;

namespace F1RaceLens.Api.Tests;

public sealed partial class StorageTests
{
    private static RaceForecast TestRace() => new(18, "Next GP", new DateOnly(2026, 10, 25), [
        new("a", "A", 20, 1.2, 75, 90, 5), new("b", "B", 15, 1.8, 25, 90, 5)]);
    private static PredictionResponse TestForecast() => new(2026, Now.AddMinutes(-5), false, "Test", "ready", null,
        17, 1, 0, 4000, "test-v1", [], [], Races: [TestRace()]) {
            GeneratedAt = Now, InputsJson = "{\"cutoff\":17}",
            Schedule = [new(18, false, "test", "Next GP", new DateOnly(2026, 10, 25), Now.AddDays(6))]
        };

    [Fact]
    public void Race_scoring_has_known_brier_and_order_error_and_refuses_partial_fields()
    {
        DriverResult Actual(int place, string id) => new(place, place.ToString(), id, id, null, null, null, null, null, "Finished", null, null, null, null);
        var score = ForecastJournal.Score(TestRace(), [Actual(1, "b"), Actual(2, "a")]);
        Assert.NotNull(score); Assert.Equal(1, score.Value.Mae); Assert.Equal(1.125, score.Value.Brier); Assert.False(score.Value.Hit);
        Assert.Null(ForecastJournal.Score(TestRace(), [Actual(1, "a")]));
        Assert.Null(ForecastJournal.Score(TestRace(), [Actual(1, "a"), Actual(2, "other")]));
        Assert.Throws<InvalidDataException>(() => ForecastJournal.ValidateRace(TestRace() with { Drivers = [new("a", "A", 1, 1, double.NaN, 1, 1)] }));
    }

    [PostgreSqlFact]
    public async Task Forecast_journal_is_concurrent_idempotent_preserves_first_seal_and_excludes_late_observations()
    {
        await using var fixture = await DatabaseFixture.Create();
        var clock = new FixedClock(Now); var journal = new ForecastJournal(fixture.Factory, clock);
        var forecast = TestForecast();
        var saved = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => journal.SaveAsync("script", forecast, default)));
        Assert.All(saved, x => Assert.Equal(saved[0].RunId, x.RunId));
        var revised = forecast with { GeneratedAt = Now.AddMinutes(1), Races = [TestRace() with { Name = "Revised" }] };
        clock.Now = Now.AddMinutes(2);
        await journal.SaveAsync("script", revised, default);
        await journal.SaveAsync("ml", forecast, default);
        clock.Now = Now.AddDays(6); // At the start is already too late to seal.
        await journal.SaveAsync("late", forecast, default);
        await journal.SaveAsync("stale", forecast with { IsStale = true }, default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Equal(4, await db.ForecastRuns.CountAsync());
        Assert.Equal(2, await db.ForecastSeals.CountAsync());
        var first = await db.ForecastSeals.SingleAsync(x => x.Engine == "script");
        Assert.Equal(Now, first.FrozenAt); Assert.Equal(saved[0].RunId, first.RunId);
        Assert.DoesNotContain(await db.ForecastSeals.ToListAsync(), x => x.Engine == "late");
        var source = await db.ForecastRuns.SingleAsync(x => x.Id == first.RunId);
        Assert.Equal(ForecastJournal.Hash(forecast.InputsJson!), source.InputHash);
        Assert.Equal(forecast.InputsJson, source.Inputs);
        Assert.Equal(ForecastJournal.Hash(source.Output), source.OutputHash);
    }

    [PostgreSqlFact]
    public async Task Evaluation_waits_for_results_deduplicates_scores_and_appends_official_revisions()
    {
        await using var fixture = await DatabaseFixture.Create();
        var clock = new FixedClock(Now); var journal = new ForecastJournal(fixture.Factory, clock);
        var saved = await journal.SaveAsync("ml", TestForecast(), default);
        var revision = false; var calls = 0;
        string Page() {
            calls++;
            var participants = (revision ? new[] { "b", "a" } : new[] { "a", "b" })
                .Select((id, i) => new { position = (i + 1).ToString(), Driver = new { driverId = id, givenName = id, familyName = "Driver" } });
            return System.Text.Json.JsonSerializer.Serialize(new { MRData = new { total = "2", RaceTable = new { season = "2026", round = "18",
                Races = new[] { new { season = "2026", round = "18", Results = participants } } } } });
        }
        ResultsService Service() => new(new JolpicaResultsClient(new ForecastTestFactory(Page)), clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ResultsService>.Instance);
        Assert.Equal("pending", Assert.Single((await journal.AssessAsync(Service(), default)).Rows).Status);
        Assert.Equal(0, calls);
        clock.Now = Now.AddDays(7);
        var first = Assert.Single((await journal.AssessAsync(Service(), default)).Rows);
        Assert.Equal("scored", first.Status); Assert.True(first.WinnerHit); Assert.Equal(.125, first.WinnerBrier);
        await journal.AssessAsync(Service(), default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.ForecastScores.CountAsync());
        revision = true;
        var second = Assert.Single((await journal.AssessAsync(Service(), default)).Rows);
        Assert.False(second.WinnerHit); Assert.Equal(1.125, second.WinnerBrier);
        Assert.Equal(2, await db.ForecastScores.CountAsync());
        Assert.Equal(saved.RunId, (await db.ForecastSeals.SingleAsync()).RunId);
    }

    private sealed class ForecastTestFactory(Func<string> page) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ForecastTestHandler(page)) { BaseAddress = new Uri("https://api.jolpi.ca/ergast/f1/") };
    }
    private sealed class ForecastTestHandler(Func<string> page) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(page(), System.Text.Encoding.UTF8, "application/json") });
    }
}
