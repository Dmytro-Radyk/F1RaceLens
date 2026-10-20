using System.Collections.Concurrent;
using System.Text;

namespace F1RaceLens.Api.Storage;

public sealed record CapturedSource(string Url, string Body, DateTimeOffset RetrievedAt);
public sealed class SourceCaptureHandler(IHttpContextAccessor accessor, TimeProvider clock) : DelegatingHandler
{
    public const string CaptureKey = "F1RaceLens.SourcePages";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var response = await base.SendAsync(request, token);
        if (response.IsSuccessStatusCode && accessor.HttpContext?.Items[CaptureKey] is ConcurrentBag<CapturedSource> pages)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            if (bytes.Length > 4 * 1024 * 1024) throw new HttpRequestException("Source page too large.");
            pages.Add(new CapturedSource(request.RequestUri!.AbsoluteUri, Encoding.UTF8.GetString(bytes), clock.GetUtcNow()));
        }
        return response;
    }
}
