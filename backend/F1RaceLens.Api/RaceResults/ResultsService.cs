using System.Collections.Concurrent;

namespace F1RaceLens.Api.RaceResults;

public sealed class ResultsService(JolpicaResultsClient source, TimeProvider clock, ILogger<ResultsService> logger)
{
    private sealed record Entry(SessionResultsResponse Response, DateTimeOffset RefreshAfter, DateTimeOffset RetainUntil);
    private readonly ConcurrentDictionary<(int, int, string), Entry> cache = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<SessionResultsResponse> GetAsync(int season, int round, string kind, CancellationToken cancellationToken)
    {
        var key = (season, round, kind);
        if (cache.TryGetValue(key, out var saved) && Fresh(saved)) return saved.Response;
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out saved) && Fresh(saved)) return saved.Response;
            try
            {
                var rows = await source.FetchAsync(season, round, kind, cancellationToken);
                var now = clock.GetUtcNow();
                var response = new SessionResultsResponse(season, round, kind, now, false, "Jolpica", rows);
                cache[key] = new Entry(response, now.AddMinutes(rows.Count == 0 ? 1 : 15), now.AddHours(24));
                return response;
            }
            catch (ResultsSourceException error) when (saved is not null && saved.RetainUntil > clock.GetUtcNow())
            {
                logger.LogWarning(error, "Serving cached results for {Season}/{Round}/{Kind}.", season, round, kind);
                var stale = saved with { Response = saved.Response with { IsStale = true }, RefreshAfter = clock.GetUtcNow().AddMinutes(1) };
                cache[key] = stale;
                return stale.Response;
            }
        }
        finally { refreshLock.Release(); }
    }

    private bool Fresh(Entry entry) => entry.RefreshAfter > clock.GetUtcNow() && entry.RetainUntil > clock.GetUtcNow();
}
