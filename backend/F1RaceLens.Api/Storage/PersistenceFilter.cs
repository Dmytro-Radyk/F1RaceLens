using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace F1RaceLens.Api.Storage;

public sealed class PersistenceFilter(SnapshotStore store, TimeProvider clock) : IEndpointFilter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await InvokeCoreAsync(context, next); }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            context.HttpContext.RequestServices.GetService<ILogger<PersistenceFilter>>()?
                .LogWarning(error, "Persistent storage is temporarily unavailable.");
            return Results.Problem(statusCode: 503, title: "Persistent storage is temporarily unavailable.");
        }
    }
    private async ValueTask<object?> InvokeCoreAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method)) return await next(context);
        var key = http.Request.Path.Value!.ToLowerInvariant();
        int? expected = key.Contains("/current/", StringComparison.Ordinal) ? clock.GetUtcNow().Year : null;
        if (expected is not null) key = key.Replace("/current/", $"/{expected}/", StringComparison.Ordinal);
        var saved = await store.ReadAsync(key, expected, http.RequestAborted);
        if (saved is not null && saved.RefreshAfter > clock.GetUtcNow())
            return Results.Content(saved.Payload, "application/json", statusCode: 200);
        var pages = new ConcurrentBag<CapturedSource>();
        http.Items[SourceCaptureHandler.CaptureKey] = pages;
        var result = await next(context);
        if (result is IValueHttpResult { Value: not null } value && result is IStatusCodeHttpResult { StatusCode: 200 })
        {
            var payload = JsonSerializer.Serialize(value.Value, value.Value.GetType(), Json);
            using var parsed = JsonDocument.Parse(payload);
            if (expected is null || parsed.RootElement.GetProperty("season").GetInt32() == expected)
                await store.SaveAsync(key, payload, pages.ToArray(), http.RequestAborted);
            return result;
        }
        if (result is IStatusCodeHttpResult { StatusCode: 503 } && saved is not null
            && (saved.Season < clock.GetUtcNow().Year || saved.FetchedAt.AddHours(24) > clock.GetUtcNow()))
        {
            var payload = JsonNode.Parse(saved.Payload)!.AsObject();
            payload["isStale"] = true;
            http.Response.Headers.Remove("Retry-After");
            return Results.Json(payload, statusCode: 200);
        }
        return result;
    }
}
