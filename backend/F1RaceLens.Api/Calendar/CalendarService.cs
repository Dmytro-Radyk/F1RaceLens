using System.Collections.Concurrent;
using System.Globalization;

namespace F1RaceLens.Api.Calendar;

public sealed class CalendarService(
    JolpicaCalendarClient source,
    TimeProvider clock,
    ILogger<CalendarService> logger)
{
    private sealed record CacheEntry(CalendarResponse Response, DateTimeOffset RefreshAfter, DateTimeOffset RetainUntil);
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<CalendarResponse> GetAsync(string season, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(season, out var cached) && cached.RefreshAfter > clock.GetUtcNow() && cached.RetainUntil > clock.GetUtcNow()) return cached.Response;

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(season, out cached) && cached.RefreshAfter > clock.GetUtcNow() && cached.RetainUntil > clock.GetUtcNow()) return cached.Response;
            try
            {
                var schedule = await source.FetchAsync(season, cancellationToken);
                var now = clock.GetUtcNow();
                var response = new CalendarResponse(schedule.Season, now, false, "Jolpica", schedule.Races);
                Store(season, new CacheEntry(response, now.AddMinutes(15), now.AddHours(24)));
                return response;
            }
            catch (CalendarSourceException error) when (cached is not null && cached.RetainUntil > clock.GetUtcNow())
            {
                logger.LogWarning(error, "Calendar source unavailable; serving cached season {Season}.", season);
                var stale = cached with { Response = cached.Response with { IsStale = true }, RefreshAfter = clock.GetUtcNow().AddMinutes(1) };
                Store(season, stale);
                return stale.Response;
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private void Store(string requestedSeason, CacheEntry entry)
    {
        cache[requestedSeason] = entry;
        cache[entry.Response.Season.ToString(CultureInfo.InvariantCulture)] = entry;
        if (cache.TryGetValue("current", out var current) && current.Response.Season == entry.Response.Season)
            cache["current"] = entry;
    }
}
