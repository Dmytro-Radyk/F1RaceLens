namespace F1RaceLens.Api.Predictions.Updates;

public sealed class SourceRateLimitedException() : Exception("Source requests are temporarily rate limited.");

public sealed class UpdateRequestGate(TimeProvider clock, TimeSpan? interval = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset last = DateTimeOffset.MinValue;
    public async Task WaitAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var delay = last + (interval ?? TimeSpan.FromMilliseconds(600)) - clock.GetUtcNow();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, clock, token);
            last = clock.GetUtcNow();
        }
        finally { gate.Release(); }
    }
}

public sealed class JolpicaUpdateHandler(UpdateRequestGate gate, TimeProvider clock) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            await gate.WaitAsync(token);
            var response = await base.SendAsync(request, token);
            var rateLimited = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
            if (request.Method != HttpMethod.Get || !rateLimited && response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
                return response;
            var wait = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } time ? time - clock.GetUtcNow() : TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)));
            if (attempt >= 3 || wait > TimeSpan.FromMinutes(2))
            {
                if (!rateLimited) return response;
                response.Dispose(); throw new SourceRateLimitedException();
            }
            response.Dispose();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, token);
        }
    }
}
