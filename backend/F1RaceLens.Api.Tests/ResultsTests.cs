using System.Net;
using System.Text;
using F1RaceLens.Api.RaceResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1RaceLens.Api.Tests;

public sealed class ResultsTests
{
    private const string Row = """{"position":"1","positionText":"1","points":"4.5","Driver":{"driverId":"farina","givenName":"Nino","familyName":"Farina"},"status":"Engine","grid":"0","laps":"12","Q1":"1:20.000"}""";
    private static string Sample(string kind = "Results", string row = Row) =>
        """
        {"MRData":{"total":"1","RaceTable":{"season":"1950","round":"1","Races":[{"season":"1950","round":"1",
        """
        + $"\"{kind}\":[{row}]" + "}]}}}";
    private const string Empty = """{"MRData":{"total":"0","RaceTable":{"season":"1950","round":"1","Races":[]}}}""";

    [Theory]
    [InlineData("race", "Results", "results")]
    [InlineData("qualifying", "QualifyingResults", "qualifying")]
    [InlineData("sprint", "SprintResults", "sprint")]
    public async Task MapsSessionTypesAndPreservesHistoricalMissingValues(string kind, string field, string path)
    {
        var factory = new Factory((request, _) =>
        {
            Assert.Equal($"https://api.jolpi.ca/ergast/f1/1950/1/{path}.json?limit=100", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json(Sample(field)));
        });
        var row = Assert.Single(await new JolpicaResultsClient(factory).FetchAsync(1950, 1, kind, CancellationToken.None));
        Assert.Equal("Nino Farina", row.DriverName);
        Assert.Equal(4.5m, row.Points);
        Assert.Equal(0, row.Grid);
        Assert.Equal("Engine", row.Status);
        Assert.Null(row.Team);
        Assert.Null(row.Time);
        Assert.Null(row.DriverCode);
        Assert.Equal("1:20.000", row.Q1);
        Assert.Null(row.Q2);
        Assert.Null(row.Q3);
    }

    [Fact]
    public async Task AllowsEmptyResults() =>
        Assert.Empty(await Source(() => Json(Empty)).FetchAsync(1950, 1, "race", CancellationToken.None));

    [Fact]
    public async Task NormalizesBlankQualifyingTimes()
    {
        var row = Assert.Single(await Source(() => Json(Sample("QualifyingResults", Row.Replace("1:20.000", " ")))).FetchAsync(1950, 1, "qualifying", CancellationToken.None));
        Assert.Null(row.Q1);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"MRData\":{\"total\":\"1\",\"RaceTable\":{\"season\":\"1950\",\"round\":\"1\",\"Races\":[]}}}")]
    public async Task RejectsMalformedAndTruncatedResponses(string payload) =>
        await Assert.ThrowsAsync<ResultsSourceException>(() => Source(() => Json(payload)).FetchAsync(1950, 1, "race", CancellationToken.None));

    [Fact]
    public async Task RejectsWrongRaceAndInvalidRows()
    {
        foreach (var payload in new[] { Sample().Replace("\"1950\"", "\"1951\""), Sample().Replace("\"round\":\"1\"", "\"round\":\"2\""), Sample(row: Row.Replace("\"position\":\"1\"", "\"position\":\"0\"")), Sample().Replace("\"total\":\"1\"", "\"total\":\"2\"") })
            await Assert.ThrowsAsync<ResultsSourceException>(() => Source(() => Json(payload)).FetchAsync(1950, 1, "race", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ConvertsProviderErrors(HttpStatusCode code) =>
        await Assert.ThrowsAsync<ResultsSourceException>(() => Source(() => new HttpResponseMessage(code)).FetchAsync(1950, 1, "race", CancellationToken.None));

    [Fact]
    public async Task CoalescesConcurrentRequestsAndRetainsStaleSnapshotOnlyFor24Hours()
    {
        var calls = 0;
        var clock = new Clock();
        var source = Source(() => ++calls == 1 ? Json(Sample()) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = new ResultsService(source, clock, NullLogger<ResultsService>.Instance);
        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetAsync(1950, 1, "race", CancellationToken.None)));
        Assert.Equal(1, calls);
        clock.Now = clock.Now.AddMinutes(16);
        var stale = await service.GetAsync(1950, 1, "race", CancellationToken.None);
        Assert.True(stale.IsStale);
        Assert.Equal(replies[0].UpdatedAt, stale.UpdatedAt);
        await service.GetAsync(1950, 1, "race", CancellationToken.None);
        Assert.Equal(2, calls);
        clock.Now = clock.Now.AddHours(24);
        await Assert.ThrowsAsync<ResultsSourceException>(() => service.GetAsync(1950, 1, "race", CancellationToken.None));
    }

    [Fact]
    public async Task RefreshesEmptyResultsAfterOneMinute()
    {
        var calls = 0;
        var clock = new Clock();
        var service = new ResultsService(Source(() => ++calls == 1 ? Json(Empty) : Json(Sample())), clock, NullLogger<ResultsService>.Instance);
        Assert.Empty((await service.GetAsync(1950, 1, "race", CancellationToken.None)).Entries);
        clock.Now = clock.Now.AddSeconds(61);
        Assert.Single((await service.GetAsync(1950, 1, "race", CancellationToken.None)).Entries);
    }

    [Fact]
    public async Task PropagatesUserCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new JolpicaResultsClient(new Factory((_, token) => Task.FromCanceled<HttpResponseMessage>(token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchAsync(1950, 1, "race", cancellation.Token));
    }

    private static JolpicaResultsClient Source(Func<HttpResponseMessage> response) => new(new Factory((_, _) => Task.FromResult(response())));
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
