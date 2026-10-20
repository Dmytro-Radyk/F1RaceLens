using System.Net;
using System.Net.Http.Headers;
using F1RaceLens.Api.Predictions.Updates;

namespace F1RaceLens.Api.Tests;

public sealed class UpdateThrottleTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Retry_after_zero_allows_a_bounded_retry(HttpStatusCode status)
    {
        var calls = 0;
        using var client = new HttpClient(new JolpicaUpdateHandler(new(TimeProvider.System, TimeSpan.Zero), TimeProvider.System) {
            InnerHandler = new Handler(() => ++calls == 1 ? Response(status, TimeSpan.Zero) : new(HttpStatusCode.OK)) });
        using var response = await client.GetAsync("https://api.jolpi.ca/test");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Long_cooldown_is_not_shortened_and_retries_have_a_limit()
    {
        var calls = 0;
        using var client = new HttpClient(new JolpicaUpdateHandler(new(TimeProvider.System, TimeSpan.Zero), TimeProvider.System) {
            InnerHandler = new Handler(() => { calls++; return Response(HttpStatusCode.TooManyRequests, TimeSpan.FromHours(1)); }) });
        await Assert.ThrowsAsync<SourceRateLimitedException>(() => client.GetAsync("https://api.jolpi.ca/test"));
        Assert.Equal(1, calls);
        calls = 0;
        using var limited = new HttpClient(new JolpicaUpdateHandler(new(TimeProvider.System, TimeSpan.Zero), TimeProvider.System) {
            InnerHandler = new Handler(() => { calls++; return Response(HttpStatusCode.TooManyRequests, TimeSpan.Zero); }) });
        await Assert.ThrowsAsync<SourceRateLimitedException>(() => limited.GetAsync("https://api.jolpi.ca/test"));
        Assert.Equal(4, calls);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, TimeSpan wait)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(wait); return response;
    }
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response());
    }
}
