using System.Net;
using System.Text;
using F1RaceLens.Api.Drivers;
using F1RaceLens.Api.Standings;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1RaceLens.Api.Tests;

public sealed class DriverTests
{
    private const string Bio = """{"MRData":{"total":"1","DriverTable":{"Drivers":[{"driverId":"test","givenName":"Test","familyName":"Driver","dateOfBirth":"1990-01-01"}]}}}""";
    private const string Result = """{"position":"2","positionText":"2","points":"18","grid":"3","status":"Finished","Driver":{"driverId":"test","givenName":"Test","familyName":"Driver"}}""";
    private static string Races(int offset = 0, int total = 1, string result = Result, int round = 1) =>
        $$$$"""
        {"MRData":{"offset":"{{{{offset}}}}","total":"{{{{total}}}}","RaceTable":{"season":"2025","driverId":"test","Races":[{"season":"2025","round":"{{{{round}}}}","raceName":"Test Grand Prix","date":"2025-03-01","Circuit":{"circuitName":"Test Circuit"},"Results":[{{{{result}}}}]}]}}}
        """;
    private const string Ranking = """{"MRData":{"total":"1","offset":"0","StandingsTable":{"season":"2025","round":"24","StandingsLists":[{"season":"2025","round":"24","DriverStandings":[{"position":"1","positionText":"1","points":"100","wins":"3","Driver":{"driverId":"test","givenName":"Test","familyName":"Driver"},"Constructors":[{"name":"Team A"}]}]}]}}}""";

    [Fact]
    public async Task MapsBiographyAndRaceWithoutInventingMissingData()
    {
        var source = Source(request => Json(request.RequestUri!.AbsolutePath.EndsWith("test.json") ? Bio : Races()));
        var data = (await source.FetchAsync(2025, "test", CancellationToken.None))!;
        Assert.Equal(new DateOnly(1990, 1, 1), data.Driver.DateOfBirth);
        Assert.Null(data.Driver.Code);
        Assert.Null(data.Driver.Nationality);
        var row = Assert.Single(data.Races);
        Assert.Equal(1, row.Round);
        Assert.Equal(2, row.Result.Position);
        Assert.Equal(3, row.Result.Grid);
        Assert.Null(row.Result.Team);
    }

    [Fact]
    public async Task UnknownDriverDoesNotRequestRaceResults()
    {
        var calls = 0;
        var source = Source(_ => { calls++; return Json("""{"MRData":{"total":"0","DriverTable":{"Drivers":[]}}}"""); });
        Assert.Null(await source.FetchAsync(2025, "unknown", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SupportsAnExistingDriverWithNoResultsInSeason()
    {
        var source = Source(request => Json(request.RequestUri!.AbsolutePath.EndsWith("test.json") ? Bio : """{"MRData":{"offset":"0","total":"0","RaceTable":{"season":"2025","driverId":"test","Races":[]}}}"""));
        Assert.Empty((await source.FetchAsync(2025, "test", CancellationToken.None))!.Races);
    }

    [Fact]
    public async Task FetchesAllPagesAndPreservesMultipleResultsForOneRound()
    {
        var calls = 0;
        var source = Source(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("test.json")) return Json(Bio);
            var offset = calls++;
            Assert.Contains($"offset={offset}", request.RequestUri.Query);
            return Json(Races(offset, 2, offset == 0 ? Result : Result.Replace("\"position\":\"2\"", "\"position\":\"4\"")));
        });
        var data = (await source.FetchAsync(2025, "test", CancellationToken.None))!;
        Assert.Equal(2, data.Races.Count);
        Assert.All(data.Races, race => Assert.Equal(1, race.Round));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task RejectsMalformedSource(string value) =>
        await Assert.ThrowsAsync<DriverSourceException>(() => Source(_ => Json(value)).FetchAsync(2025, "test", CancellationToken.None));

    [Fact]
    public async Task RejectsWrongIdentitySeasonAndTruncatedPagination()
    {
        foreach (var bad in new[] { Races().Replace("\"2025\"", "\"2024\""), Races().Replace("\"driverId\":\"test\"", "\"driverId\":\"wrong\""), Races(offset: 1), Races(total: 2) })
        {
            var source = Source(request => Json(request.RequestUri!.AbsolutePath.EndsWith("test.json") ? Bio : bad));
            await Assert.ThrowsAsync<DriverSourceException>(() => source.FetchAsync(2025, "test", CancellationToken.None));
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ConvertsProviderErrors(HttpStatusCode status) =>
        await Assert.ThrowsAsync<DriverSourceException>(() => Source(_ => new HttpResponseMessage(status)).FetchAsync(2025, "test", CancellationToken.None));

    [Fact]
    public async Task ProfileUsesChampionshipPointsAndCacheAliasesWithoutSummingRacePoints()
    {
        var calls = 0;
        var factory = new Factory((request, _) =>
        {
            calls++;
            return Task.FromResult(Json(request.RequestUri!.AbsolutePath.Contains("driverStandings") ? Ranking : request.RequestUri.AbsolutePath.EndsWith("test.json") ? Bio : Races()));
        });
        var clock = new Clock();
        var ranking = new StandingsService(new JolpicaStandingsClient(factory), clock, NullLogger<StandingsService>.Instance);
        var service = new DriverService(new JolpicaDriverClient(factory), ranking, clock, NullLogger<DriverService>.Instance);
        var data = (await service.GetAsync("current", "test", CancellationToken.None))!;
        Assert.Equal(100m, data.Standing!.Points);
        Assert.Equal(18m, Assert.Single(data.Races).Result.Points);
        Assert.Equal(24, data.StandingsRound);
        Assert.Same(data, await service.GetAsync("2025", "test", CancellationToken.None));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task StaleProfileDoesNotExtendRetention()
    {
        var fail = false;
        var factory = new Factory((request, _) => Task.FromResult(fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json(request.RequestUri!.AbsolutePath.Contains("driverStandings") ? Ranking : request.RequestUri.AbsolutePath.EndsWith("test.json") ? Bio : Races())));
        var clock = new Clock();
        var ranking = new StandingsService(new JolpicaStandingsClient(factory), clock, NullLogger<StandingsService>.Instance);
        var service = new DriverService(new JolpicaDriverClient(factory), ranking, clock, NullLogger<DriverService>.Instance);
        var original = (await service.GetAsync("2025", "test", CancellationToken.None))!;
        fail = true;
        clock.Now = clock.Now.AddHours(24).AddSeconds(-10);
        var stale = (await service.GetAsync("2025", "test", CancellationToken.None))!;
        Assert.True(stale.IsStale);
        Assert.Equal(original.UpdatedAt, stale.UpdatedAt);
        clock.Now = clock.Now.AddSeconds(20);
        await Assert.ThrowsAsync<StandingsSourceException>(() => service.GetAsync("2025", "test", CancellationToken.None));
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var source = new JolpicaDriverClient(new Factory((_, token) => Task.FromCanceled<HttpResponseMessage>(token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.FetchAsync(2025, "test", cancellation.Token));
    }

    [Fact]
    public async Task NewlyAssembledProfileCannotExtendStaleRankingLifetime()
    {
        var rankingFails = false;
        var factory = new Factory((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("driverStandings")
            ? rankingFails ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(Ranking)
            : Json(request.RequestUri.AbsolutePath.EndsWith("test.json") ? Bio : Races())));
        var clock = new Clock();
        var ranking = new StandingsService(new JolpicaStandingsClient(factory), clock, NullLogger<StandingsService>.Instance);
        await ranking.GetAsync("2025", "drivers", CancellationToken.None);
        clock.Now = clock.Now.AddHours(24).AddSeconds(-10);
        rankingFails = true;
        var service = new DriverService(new JolpicaDriverClient(factory), ranking, clock, NullLogger<DriverService>.Instance);
        Assert.True((await service.GetAsync("2025", "test", CancellationToken.None))!.IsStale);
        clock.Now = clock.Now.AddSeconds(20);
        await Assert.ThrowsAsync<StandingsSourceException>(() => service.GetAsync("2025", "test", CancellationToken.None));
    }

    private static JolpicaDriverClient Source(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Factory((request, _) => Task.FromResult(response(request))));
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
        public DateTimeOffset Now { get; set; } = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
