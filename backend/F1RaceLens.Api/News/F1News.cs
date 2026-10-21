using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace F1RaceLens.Api.News;

public sealed record NewsArticle(string Title, string Url, DateTimeOffset PublishedAt, string? ImageUrl = null);
public sealed record NewsFeed(string Source, string SourceUrl, string Language, DateTimeOffset UpdatedAt,
    bool IsStale, IReadOnlyList<NewsArticle> Articles);

public sealed class F1News(IHttpClientFactory clients, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private NewsFeed? cached;
    private DateTimeOffset retryAt;

    public async Task<NewsFeed?> ReadAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow();
            if (now < retryAt) return Available(now);
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(TimeSpan.FromSeconds(10));
                var fetchToken = budget.Token;
                using var response = await clients.CreateClient("F1News").GetAsync(
                    "https://feeds.bbci.co.uk/sport/formula1/rss.xml", HttpCompletionOption.ResponseHeadersRead, fetchToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 1_048_576) throw new InvalidDataException();
                await using var stream = await response.Content.ReadAsStreamAsync(fetchToken);
                using var buffer = new MemoryStream();
                var bytes = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(bytes, fetchToken)) > 0)
                {
                    if (buffer.Length + count > 1_048_576) throw new InvalidDataException();
                    buffer.Write(bytes, 0, count);
                }
                buffer.Position = 0;
                var articles = Parse(buffer, now);
                if (articles.Count == 0) throw new InvalidDataException();
                cached = new("BBC Sport", "https://www.bbc.com/sport/formula1", "en", now, false, articles);
                retryAt = now.AddMinutes(10);
                return cached;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or XmlException
                || error is OperationCanceledException && !token.IsCancellationRequested)
            {
                retryAt = now.AddMinutes(1);
                return Available(now);
            }
        }
        finally { gate.Release(); }
    }

    private NewsFeed? Available(DateTimeOffset now) => cached is null || now - cached.UpdatedAt > TimeSpan.FromHours(24)
        ? null : cached with { IsStale = now - cached.UpdatedAt >= TimeSpan.FromMinutes(10) };

    public static IReadOnlyList<NewsArticle> Parse(Stream stream, DateTimeOffset now)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "rss" || document.Root.Element("channel") is not { } channel)
            throw new XmlException("Invalid feed.");
        var articles = new List<NewsArticle>();
        foreach (var item in channel.Elements("item"))
        {
            var title = WebUtility.HtmlDecode(item.Element("title")?.Value ?? "");
            title = Regex.Replace(title, "<[^>]*>", "", RegexOptions.None, TimeSpan.FromSeconds(1));
            title = Regex.Replace(title, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
            var link = item.Element("link")?.Value.Trim();
            if (title.Length is < 1 or > 500 || !Uri.TryCreate(link, UriKind.Absolute, out var uri)
                || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
                || uri.Host is not ("www.bbc.com" or "www.bbc.co.uk" or "bbc.com" or "bbc.co.uk")
                || !uri.AbsolutePath.StartsWith("/sport/formula1/", StringComparison.Ordinal)
                || !DateTimeOffset.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var published) || published > now.AddMinutes(5)) continue;
            var url = uri.GetLeftPart(UriPartial.Path);
            var thumbnail = item.Element(XName.Get("thumbnail", "http://search.yahoo.com/mrss/"))?.Attribute("url")?.Value;
            var image = Uri.TryCreate(thumbnail, UriKind.Absolute, out var imageUri)
                && imageUri.Scheme == "https" && imageUri.Host == "ichef.bbci.co.uk"
                && imageUri.IsDefaultPort && imageUri.UserInfo.Length == 0 ? imageUri.AbsoluteUri : null;
            articles.Add(new(title, url, published.ToUniversalTime(), image));
        }
        return articles.DistinctBy(article => article.Url).OrderByDescending(article => article.PublishedAt).Take(30).ToArray();
    }
}
