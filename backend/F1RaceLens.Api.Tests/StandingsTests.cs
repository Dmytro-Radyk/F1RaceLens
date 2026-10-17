using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using F1RaceLens.Api.Standings;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1RaceLens.Api.Tests;

public sealed class StandingsTests
{
    private const string Driver = """{"position":"1","positionText":"1","points":"4.5","wins":"0","Driver":{"driverId":"test","givenName":"Test","familyName":"Driver"},"Constructors":[{"name":"Team A"},{"name":"Team B"}]}""";
    private const string Constructor = """{"positionText":"E","points":"-5.5","wins":"0","Constructor":{"constructorId":"test","name":"Test Team"}}""";

    private static string Payload(string kind = "DriverStandings", string[]? rows = null, int? total = null, int offset = 0, int year = 2026)
    {
        rows ??= [Driver];
        var rowCount = total ?? rows.Length;
        var list = new JsonObject { ["season"] = year.ToString(), ["round"] = "17", [kind] = new JsonArray(rows.Select(row => JsonNode.Parse(row)).ToArray()) };
        return new JsonObject { ["MRData"] = new JsonObject {
            ["total"] = rowCount.ToString(), ["offset"] = offset.ToString(),
            ["StandingsTable"] = new JsonObject { ["season"] = year.ToString(), ["round"] = rowCount == 0 ? null : "17",
                ["StandingsLists"] = rowCount == 0 ? new JsonArray() : new JsonArray(list) }
        } }.ToJsonString();
    }

    [Fact]
    public async Task MapsCurrentDriverAndAllSeasonTeamsWithoutInventingCode()
    {
        var data = await Source(() => Json(Payload())).FetchAsync("current", "drivers", CancellationToken.None);
        var row = Assert.Single(data.Entries);
        Assert.Equal(2026, data.Season);
        Assert.Equal(17, data.Round);
        Assert.Equal("Test Driver", row.Name);
        Assert.Equal(["Team A", "Team B"], row.Teams);
        Assert.Equal(4.5m, row.Points);
        Assert.Equal(0, row.Wins);
        Assert.Null(row.Code);
    }

    [Fact]
    public async Task PreservesExcludedConstructorsAndNegativePoints()
    {
        var row = Assert.Single((await Source(() => Json(Payload("ConstructorStandings", [Constructor])))
            .FetchAsync("2026", "constructors", CancellationToken.None)).Entries);
        Assert.Null(row.Position);
        Assert.Equal("E", row.PositionText);
        Assert.Equal(-5.5m, row.Points);
        Assert.Empty(row.Teams);
    }

    [Fact]
    public async Task LoadsAllPagesAndSortsByOfficialPosition()
    {
        var calls = 0;
        var client = new JolpicaStandingsClient(new Factory((request, _) =>
        {
            var offset = calls++;
            Assert.Contains($"offset={offset}", request.RequestUri!.Query);
            var row = offset == 0 ? Driver.Replace("\"position\":\"1\"", "\"position\":\"2\"") : Driver.Replace("\"test\"", "\"another\"");
            return Task.FromResult(Json(Payload(rows: [row], total: 2, offset: offset)));
        }));
        var data = await client.FetchAsync("2026", "drivers", CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.Equal(["another", "test"], data.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task RejectsDuplicatesAndUnstablePagination()
    {
        foreach (var second in new[] { Payload(total: 2, offset: 1), Payload(total: 3, offset: 1), Payload(total: 2, offset: 0), Payload(rows: [], total: 2, offset: 1) })
        {
            var calls = 0;
            var source = Source(() => Json(++calls == 1 ? Payload(total: 2) : second));
            await Assert.ThrowsAsync<StandingsSourceException>(() => source.FetchAsync("2026", "drivers", CancellationToken.None));
        }
    }

    [Fact]
    public async Task SupportsAnUnavailableHistoricalConstructorChampionship()
    {
        var data = await Source(() => Json(Payload(rows: [], year: 1950))).FetchAsync("1950", "constructors", CancellationToken.None);
        Assert.Empty(data.Entries);
        Assert.Null(data.Round);
        Assert.Equal(1950, data.Season);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task RejectsMalformedData(string payload) =>
        await Assert.ThrowsAsync<StandingsSourceException>(() => Source(() => Json(payload)).FetchAsync("current", "drivers", CancellationToken.None));

    [Fact]
    public async Task RejectsWrongSeasonRoundAndInvalidMandatoryFields()
    {
        foreach (var payload in new[] { Payload(year: 2025), Payload().Replace("\"17\"", "\"0\""), Payload(rows: [Driver.Replace("\"wins\":\"0\"", "\"wins\":\"-1\"")]), Payload(rows: [Driver.Replace("\"givenName\":\"Test\"", "\"givenName\":\" \"")]) })
            await Assert.ThrowsAsync<StandingsSourceException>(() => Source(() => Json(payload)).FetchAsync("2026", "drivers", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ConvertsSourceFailures(HttpStatusCode code) =>
        await Assert.ThrowsAsync<StandingsSourceException>(() => Source(() => new HttpResponseMessage(code)).FetchAsync("2026", "drivers", CancellationToken.None));

    [Fact]
    public async Task SharesCurrentAndYearCacheCoalescesRequestsAndKeepsKindsSeparate()
    {
        var calls = 0;
        var service = Service(Source(() => Json(++calls == 1 ? Payload() : Payload("ConstructorStandings", [Constructor]))), new Clock());
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.GetAsync("current", "drivers", CancellationToken.None)));
        Assert.Same(replies[0], await service.GetAsync("2026", "drivers", CancellationToken.None));
        Assert.Equal(1, calls);
        await service.GetAsync("2026", "constructors", CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task StaleBackoffNeverExtends24HourRetention()
    {
        var calls = 0;
        var clock = new Clock();
        var service = Service(Source(() => ++calls == 1 ? Json(Payload()) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), clock);
        var original = await service.GetAsync("current", "drivers", CancellationToken.None);
        clock.Now = clock.Now.AddHours(24).AddSeconds(-10);
        var stale = await service.GetAsync("2026", "drivers", CancellationToken.None);
        Assert.True(stale.IsStale);
        Assert.Equal(original.UpdatedAt, stale.UpdatedAt);
        Assert.Same(stale, await service.GetAsync("current", "drivers", CancellationToken.None));
        Assert.Equal(2, calls);
        clock.Now = clock.Now.AddSeconds(20);
        await Assert.ThrowsAsync<StandingsSourceException>(() => service.GetAsync("current", "drivers", CancellationToken.None));
    }

    [Fact]
    public async Task RefreshesEmptySnapshotsAfterOneMinute()
    {
        var calls = 0;
        var clock = new Clock();
        var service = Service(Source(() => Json(++calls == 1 ? Payload(rows: []) : Payload())), clock);
        Assert.Empty((await service.GetAsync("2026", "drivers", CancellationToken.None)).Entries);
        clock.Now = clock.Now.AddSeconds(61);
        Assert.Single((await service.GetAsync("2026", "drivers", CancellationToken.None)).Entries);
    }

    [Fact]
    public async Task PropagatesCancellationAndConvertsTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new JolpicaStandingsClient(new Factory((_, token) => Task.FromCanceled<HttpResponseMessage>(token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchAsync("2026", "drivers", cancellation.Token));
        var timeout = new JolpicaStandingsClient(new Factory((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException())));
        await Assert.ThrowsAsync<StandingsSourceException>(() => timeout.FetchAsync("2026", "drivers", CancellationToken.None));
    }

    private static StandingsService Service(JolpicaStandingsClient source, TimeProvider clock) => new(source, clock, NullLogger<StandingsService>.Instance);
    private static JolpicaStandingsClient Source(Func<HttpResponseMessage> response) => new(new Factory((_, _) => Task.FromResult(response())));
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class Factory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(send)) { BaseAddress = new Uri("https://api.jolpi.ca/ergast/f1/") };
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
