using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using F1RaceLens.Api.Calendar;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1RaceLens.Api.Tests;

public sealed class CalendarSourceTests
{
    private const string Sample = """
        {"MRData":{"total":"1","RaceTable":{"season":"2026","Races":[{
          "season":"2026","round":"2","raceName":"Chinese Grand Prix",
          "Circuit":{"circuitId":"shanghai","circuitName":"Shanghai International Circuit","Location":{"locality":"Shanghai","country":"China"}},
          "date":"2026-03-15","time":"07:00:00Z",
          "FirstPractice":{"date":"2026-03-13","time":"03:30:00Z"},
          "SprintQualifying":{"date":"2026-03-13","time":"07:30:00Z"},
          "Sprint":{"date":"2026-03-14","time":"03:00:00Z"},
          "Qualifying":{"date":"2026-03-14","time":"07:00:00Z"}
        }]}}}
        """;

    [Fact]
    public async Task MapsSprintWeekendWithoutInventingPracticeSessions()
    {
        var result = await Source(() => Json(Sample)).FetchAsync("current", CancellationToken.None);
        var race = Assert.Single(result.Races);
        Assert.Equal(2026, result.Season);
        Assert.Equal(["FirstPractice", "SprintQualifying", "Sprint", "Qualifying", "Race"], race.Sessions.Select(item => item.Kind));
        Assert.Equal(new DateTimeOffset(2026, 3, 15, 7, 0, 0, TimeSpan.Zero), race.StartsAt);
        Assert.Equal("Спринт", race.Sessions.Single(item => item.Kind == "Sprint").Name);
    }

    [Fact]
    public async Task PreservesDatesWhenTimesAreMissing()
    {
        var data = JsonNode.Parse(Sample)!;
        var node = data["MRData"]!["RaceTable"]!["Races"]![0]!.AsObject();
        node.Remove("time");
        node["Sprint"]!.AsObject().Remove("time");
        var race = Assert.Single((await Source(() => Json(data.ToJsonString())).FetchAsync("2026", CancellationToken.None)).Races);
        Assert.Null(race.StartsAt);
        Assert.Equal(new DateOnly(2026, 3, 15), race.Date);
        Assert.Null(race.Sessions.Single(item => item.Kind == "Sprint").StartsAt);
    }

    [Fact]
    public async Task SupportsEmptyCalendars()
    {
        var result = await Source(() => Json("""{"MRData":{"total":"0","RaceTable":{"season":"2026","Races":[]}}}""")).FetchAsync("2026", CancellationToken.None);
        Assert.Empty(result.Races);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("<html>Unavailable</html>")]
    [InlineData("{\"MRData\":{\"total\":\"1\",\"RaceTable\":{\"season\":\"2026\",\"Races\":[]}}}")]
    public async Task RejectsMalformedOrIncompleteResponses(string value) =>
        await Assert.ThrowsAsync<CalendarSourceException>(() => Source(() => Json(value)).FetchAsync("current", CancellationToken.None));

    [Fact]
    public async Task RejectsUnexpectedSeasons() =>
        await Assert.ThrowsAsync<CalendarSourceException>(() => Source(() => Json(Sample)).FetchAsync("2025", CancellationToken.None));

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ConvertsProviderErrors(HttpStatusCode status) =>
        await Assert.ThrowsAsync<CalendarSourceException>(() => Source(() => new HttpResponseMessage(status)).FetchAsync("current", CancellationToken.None));

    [Fact]
    public async Task SharesCacheBetweenCurrentAndExplicitSeason()
    {
        var calls = 0;
        var service = Service(Source(() => { calls++; return Json(Sample); }), new TestClock());
        var first = await service.GetAsync("current", CancellationToken.None);
        Assert.Same(first, await service.GetAsync("2026", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CoalescesConcurrentRequests()
    {
        var calls = 0;
        var factory = new StubFactory(async (_, cancellation) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(20, cancellation);
            return Json(Sample);
        });
        var service = Service(new JolpicaCalendarClient(factory), new TestClock());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetAsync("current", CancellationToken.None)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MarksCachedDataAsStaleWithoutChangingTimestampAndBacksOff()
    {
        var clock = new TestClock();
        var calls = 0;
        var service = Service(Source(() => ++calls == 1 ? Json(Sample) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), clock);
        var original = await service.GetAsync("current", CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(16));
        var stale = await service.GetAsync("current", CancellationToken.None);
        Assert.True(stale.IsStale);
        Assert.Equal(original.UpdatedAt, stale.UpdatedAt);
        Assert.Equal(original.Races, stale.Races);
        await service.GetAsync("2026", CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DoesNotServeExpiredSnapshots()
    {
        var clock = new TestClock();
        var calls = 0;
        var service = Service(Source(() => ++calls == 1 ? Json(Sample) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), clock);
        await service.GetAsync("current", CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(25));
        await Assert.ThrowsAsync<CalendarSourceException>(() => service.GetAsync("current", CancellationToken.None));
    }

    [Fact]
    public async Task DoesNotHideCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(Source(() => Json(Sample)), new TestClock()).GetAsync("current", cancellation.Token));
    }

    [Fact]
    public async Task RetryBackoffDoesNotExtendSnapshotRetention()
    {
        var clock = new TestClock();
        var calls = 0;
        var service = Service(Source(() => ++calls == 1 ? Json(Sample) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), clock);
        await service.GetAsync("current", CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(20));
        Assert.True((await service.GetAsync("current", CancellationToken.None)).IsStale);
        clock.Advance(TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<CalendarSourceException>(() => service.GetAsync("current", CancellationToken.None));
    }

    private static CalendarService Service(JolpicaCalendarClient source, TimeProvider clock) => new(source, clock, NullLogger<CalendarService>.Instance);
    private static JolpicaCalendarClient Source(Func<HttpResponseMessage> response) => new(new StubFactory((_, _) => Task.FromResult(response())));
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now += delta;
    }
    private sealed class StubFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(response)) { BaseAddress = new Uri("https://api.jolpi.ca/ergast/f1/") };
    }
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken);
    }
}
