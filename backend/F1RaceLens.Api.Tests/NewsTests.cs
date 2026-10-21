using System.Net;
using System.Text;
using System.Xml;
using F1RaceLens.Api.News;

namespace F1RaceLens.Api.Tests;

public sealed class NewsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
    private const string Item = "<item><title><![CDATA[<b>F1</b> &amp; news]]></title><link>https://www.bbc.co.uk/sport/formula1/articles/example?tracking=1</link><pubDate>Tue, 20 Oct 2026 10:00:00 GMT</pubDate></item>";
    private static MemoryStream Xml(string text) => new(Encoding.UTF8.GetBytes(text));
    private static string Feed(string items) => $"<rss><channel>{items}</channel></rss>";

    [Fact]
    public void ParsesPlainTextDatesAndCanonicalLinksWithoutDuplicates()
    {
        using var stream = Xml(Feed(Item + Item));
        var article = Assert.Single(F1News.Parse(stream, Now));
        Assert.Equal("F1 & news", article.Title);
        Assert.Equal("https://www.bbc.co.uk/sport/formula1/articles/example", article.Url);
        Assert.Equal(TimeSpan.Zero, article.PublishedAt.Offset);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://www.bbc.co.uk.attacker.example/sport/formula1/articles/x")]
    [InlineData("https://www.bbc.co.uk/sport/football/articles/x")]
    [InlineData("https://user@www.bbc.co.uk/sport/formula1/articles/x")]
    [InlineData("https://www.bbc.co.uk:444/sport/formula1/articles/x")]
    public void RejectsNonF1AndUnsafeLinks(string link)
    {
        using var stream = Xml(Feed(Item.Replace("https://www.bbc.co.uk/sport/formula1/articles/example?tracking=1", link)));
        Assert.Empty(F1News.Parse(stream, Now));
    }

    [Fact]
    public void RejectsDtdAndFutureDates()
    {
        using var malicious = Xml("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///secret'>]><rss><channel>&x;</channel></rss>");
        Assert.Throws<XmlException>(() => F1News.Parse(malicious, Now));
        using var future = Xml(Feed(Item.Replace("Tue, 20 Oct 2026", "Tue, 20 Oct 2037")));
        Assert.Empty(F1News.Parse(future, Now));
    }

    [Theory]
    [InlineData("https://ichef.bbci.co.uk/ace/standard/240/image.jpg", true)]
    [InlineData("https://ichef.bbci.co.uk.attacker.example/image.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    public void OnlyAcceptsPublisherThumbnailHosts(string image, bool accepted)
    {
        var item = Item.Replace("</item>", $"<media:thumbnail xmlns:media='http://search.yahoo.com/mrss/' url='{image}' /></item>");
        using var stream = Xml(Feed(item));
        var article = Assert.Single(F1News.Parse(stream, Now));
        Assert.Equal(accepted ? image : null, article.ImageUrl);
    }

    [Fact]
    public async Task CachesAndPreservesLastSuccessWithBoundedStaleness()
    {
        var clock = new Clock();
        var calls = 0;
        var factory = new Factory(() => ++calls == 1
            ? new(HttpStatusCode.OK) { Content = new StringContent(Feed(Item)) }
            : new(HttpStatusCode.ServiceUnavailable));
        var service = new F1News(factory, clock);
        var first = await service.ReadAsync(default);
        Assert.NotNull(first);
        Assert.False(first.IsStale);
        await service.ReadAsync(default);
        Assert.Equal(1, calls);
        clock.Now = Now.AddMinutes(11);
        var stale = await service.ReadAsync(default);
        Assert.NotNull(stale);
        Assert.True(stale.IsStale);
        Assert.Equal(first.UpdatedAt, stale.UpdatedAt);
        await service.ReadAsync(default);
        Assert.Equal(2, calls);
        clock.Now = Now.AddHours(25);
        Assert.Null(await service.ReadAsync(default));
    }

    [Fact]
    public async Task MissingFeedReturnsUnavailableAndRequestCancellationPropagates()
    {
        var service = new F1News(new Factory(() => new(HttpStatusCode.BadGateway)), new Clock());
        Assert.Null(await service.ReadAsync(default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(cancelled.Token));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = NewsTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Factory(Func<HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(respond));
    }
    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond());
    }
}
