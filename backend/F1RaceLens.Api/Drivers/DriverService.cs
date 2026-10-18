using System.Collections.Concurrent;
using System.Globalization;
using F1RaceLens.Api.Standings;

namespace F1RaceLens.Api.Drivers;

public sealed class DriverService(JolpicaDriverClient source, StandingsService standings, TimeProvider clock, ILogger<DriverService> logger)
{
    private sealed record Entry(DriverProfileResponse Response, DateTimeOffset RefreshAfter, DateTimeOffset RetainUntil);
    private readonly ConcurrentDictionary<(string, string), Entry> cache = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    public async Task<DriverProfileResponse?> GetAsync(string season, string id, CancellationToken cancellationToken)
    {
        var key = (season, id);
        if (cache.TryGetValue(key, out var saved) && Fresh(saved)) return saved.Response;
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out saved) && Fresh(saved)) return saved.Response;
            try
            {
                var ranking = await standings.GetAsync(season, "drivers", cancellationToken);
                var data = await source.FetchAsync(ranking.Season, id, cancellationToken);
                if (data is null) return null;
                var now = clock.GetUtcNow();
                var response = new DriverProfileResponse(ranking.Season, now, ranking.IsStale, "Jolpica", data.Driver,
                    ranking.Round, ranking.Entries.SingleOrDefault(entry => entry.Id == id), data.Races);
                Store(key, new Entry(response, now.AddMinutes(ranking.IsStale || data.Races.Count == 0 ? 1 : 15), ranking.UpdatedAt.AddHours(24)));
                return response;
            }
            catch (Exception error) when ((error is DriverSourceException or StandingsSourceException) && saved is not null && saved.RetainUntil > clock.GetUtcNow())
            {
                logger.LogWarning(error, "Serving cached profile for {Season}/{Driver}.", season, id);
                var stale = saved with { Response = saved.Response with { IsStale = true }, RefreshAfter = clock.GetUtcNow().AddMinutes(1) };
                Store(key, stale);
                return stale.Response;
            }
        }
        finally { refreshLock.Release(); }
    }
    private bool Fresh(Entry entry) => entry.RefreshAfter > clock.GetUtcNow() && entry.RetainUntil > clock.GetUtcNow();
    private void Store((string Season, string Id) key, Entry entry)
    {
        cache[key] = entry;
        cache[(entry.Response.Season.ToString(CultureInfo.InvariantCulture), key.Id)] = entry;
        if (cache.TryGetValue(("current", key.Id), out var current) && current.Response.Season == entry.Response.Season)
            cache[("current", key.Id)] = entry;
    }
}
