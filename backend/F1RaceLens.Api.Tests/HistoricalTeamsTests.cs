using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using F1RaceLens.Api.Teams;

namespace F1RaceLens.Api.Tests;

public sealed class HistoricalTeamsTests
{
    private static string Payload(string rows, int total = 1, int offset = 0, int year = 1953, string? team = null)
    {
        var table = new JsonObject { ["season"] = year.ToString(),
            [team is null ? "Constructors" : "Drivers"] = JsonNode.Parse($"[{rows}]") };
        if (team is not null) table["constructorId"] = team;
        return new JsonObject { ["MRData"] = new JsonObject { ["total"] = total.ToString(), ["offset"] = offset.ToString(),
            [team is null ? "ConstructorTable" : "DriverTable"] = table } }.ToJsonString();
    }
    private const string Ferrari = """{"constructorId":"ferrari","name":"Ferrari"}""";

    [Fact]
    public async Task LoadsEveryPageAndCachesWithoutInventedStandings()
    {
        var calls = 0;
        var source = Source(request => {
            var offset = calls++;
            Assert.Contains($"offset={offset}", request.RequestUri!.Query);
            return Payload(offset == 0 ? Ferrari : """{"constructorId":"afm","name":"AFM"}""", 2, offset);
        });
        var result = await source.GetAsync(1953, null, CancellationToken.None);
        Assert.Equal(["afm", "ferrari"], result.Entries.Select(row => row.Id));
        Assert.Equal(result, await source.GetAsync(1953, null, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FiltersDriversByConstructorIdentity()
    {
        var source = Source(request => {
            Assert.Contains("1953/constructors/ferrari/drivers.json", request.RequestUri!.AbsolutePath);
            return Payload("""{"driverId":"ascari","givenName":"Alberto","familyName":"Ascari"}""", team: "ferrari");
        });
        var result = await source.GetAsync(1953, "ferrari", CancellationToken.None);
        Assert.Equal("Alberto Ascari", Assert.Single(result.Entries).Name);
    }

    [Theory]
    [InlineData(1952, "ferrari")]
    [InlineData(1953, "maserati")]
    public async Task RejectsMismatchedSourceFilters(int year, string team)
    {
        var source = Source(_ => Payload("", 0, year: year, team: team));
        await Assert.ThrowsAsync<ParticipantsSourceException>(() => source.GetAsync(1953, "ferrari", CancellationToken.None));
    }

    [Fact]
    public async Task RejectsTruncatedAndDuplicatedPages()
    {
        var truncated = Source(_ => Payload("", 2));
        await Assert.ThrowsAsync<ParticipantsSourceException>(() => truncated.GetAsync(1953, null, CancellationToken.None));
        var duplicate = Source(_ => Payload($"{Ferrari},{Ferrari}", 2));
        await Assert.ThrowsAsync<ParticipantsSourceException>(() => duplicate.GetAsync(1953, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(1949, null)]
    [InlineData(1958, null)]
    [InlineData(1953, "../bad")]
    public async Task RejectsInvalidInputsBeforeRequest(int year, string? team)
    {
        var source = Source(_ => throw new Exception("Unexpected HTTP request"));
        await Assert.ThrowsAsync<ArgumentException>(() => source.GetAsync(year, team, CancellationToken.None));
    }

    private static HistoricalTeams Source(Func<HttpRequestMessage, string> respond) => new(new Factory(respond), TimeProvider.System);
    private sealed class Factory(Func<HttpRequestMessage, string> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(respond)) { BaseAddress = new Uri("https://api.jolpi.ca/ergast/f1/") };
    }
    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
    }
}
