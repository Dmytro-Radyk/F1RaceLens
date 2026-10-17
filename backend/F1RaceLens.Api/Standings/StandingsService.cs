using System.Collections.Concurrent;
using System.Globalization;

namespace F1RaceLens.Api.Standings;

public sealed class StandingsService(JolpicaStandingsClient source, TimeProvider clock, ILogger<StandingsService> logger)
{
    private sealed record Entry(StandingsResponse Response, DateTimeOffset RefreshAfter, DateTimeOffset RetainUntil);
    private readonly ConcurrentDictionary<(string, string), Entry> cache = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<StandingsResponse> GetAsync(string season, string kind, CancellationToken cancellationToken)
    {
        var key = (season, kind);
        if (cache.TryGetValue(key, out var saved) && Fresh(saved)) return saved.Response;
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out saved) && Fresh(saved)) return saved.Response;
            try
            {
                var data = await source.FetchAsync(season, kind, cancellationToken);
                var now = clock.GetUtcNow();
                var response = new StandingsResponse(data.Season, data.Round, kind, now, false, "Jolpica", data.Entries);
                Store(key, new Entry(response, now.AddMinutes(data.Entries.Count == 0 ? 1 : 15), now.AddHours(24)));
                return response;
            }
            catch (StandingsSourceException error) when (saved is not null && saved.RetainUntil > clock.GetUtcNow())
            {
                logger.LogWarning(error, "Serving cached standings for {Season}/{Kind}.", season, kind);
                var stale = saved with { Response = saved.Response with { IsStale = true }, RefreshAfter = clock.GetUtcNow().AddMinutes(1) };
                Store(key, stale);
                return stale.Response;
            }
        }
        finally { refreshLock.Release(); }
    }
    private bool Fresh(Entry entry) => entry.RefreshAfter > clock.GetUtcNow() && entry.RetainUntil > clock.GetUtcNow();
    private void Store((string Season, string Kind) key, Entry entry)
    {
        cache[key] = entry;
        cache[(entry.Response.Season.ToString(CultureInfo.InvariantCulture), key.Kind)] = entry;
        if (cache.TryGetValue(("current", key.Kind), out var current) && current.Response.Season == entry.Response.Season)
            cache[("current", key.Kind)] = entry;
    }
}
