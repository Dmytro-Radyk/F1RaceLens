using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace F1RaceLens.Api.Assistant;

public static class AssistantEndpoints
{
    public static void AddAssistant(this IServiceCollection services)
    {
        services.AddSingleton<LocalChatModel>();
        services.AddSingleton<AssistantKnowledge>();
        services.AddSingleton<ExternalKnowledge>();
        services.AddHttpClient("AssistantWeb", c => {
            c.Timeout = TimeSpan.FromSeconds(8);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("F1RaceLens/1.0 (F1 public reference assistant)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .RemoveAllLoggers();
        services.AddHttpClient("AssistantModel", c => { c.BaseAddress = new("http://127.0.0.1:4310/"); c.Timeout = TimeSpan.FromSeconds(150); })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("AssistantHealth", c => { c.BaseAddress = new("http://127.0.0.1:4310/"); c.Timeout = TimeSpan.FromSeconds(2); });
        services.AddRateLimiter(o => {
            o.RejectionStatusCode = 429;
            o.AddPolicy("assistant", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions {
                    PermitLimit = 8, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));
        });
    }

    public static void MapAssistant(this WebApplication app)
    {
        app.MapGet("/api/assistant/status", async (LocalChatModel model, HttpContext http) => {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { ready = await model.ReadyAsync(http.RequestAborted), model = LocalChatModel.ModelName });
        });
        app.MapPost("/api/assistant/chat", async (ChatRequest request, AssistantKnowledge knowledge,
            ExternalKnowledge external, LocalChatModel model, HttpContext http) => {
            http.Response.Headers.CacheControl = "no-store";
            if (!ChatValidation.Valid(request)) return Results.BadRequest(new { code = "invalid-chat" });
            if (http.Request.Headers["Sec-Fetch-Site"] == "cross-site") return Results.StatusCode(403);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
            budget.CancelAfter(TimeSpan.FromSeconds(150));
            try
            {
                var reference = request.WebSearch ? null : F1Knowledge.Reference(request);
                if (reference is not null) return Results.Ok(reference);
                var webTask = external.SearchAsync(request, budget.Token);
                IReadOnlyList<ChatSource> local;
                try { local = await knowledge.SearchAsync(request, budget.Token); }
                catch (Exception error) when (error is Npgsql.NpgsqlException or IOException or HttpRequestException) { local = []; }
                var web = await webTask;
                // A small amount of external evidence survives the model's source-budget trimming.
                var sources = local.Take(1).Concat(web.Sources.Take(2)).Concat(local.Skip(1))
                    .DistinctBy(s => s.Url).Take(10).Select((s, i) => s with { Id = i + 1 }).ToArray();
                if (!await model.ReadyAsync(budget.Token)) return Results.Json(new { code = "model-unavailable" }, statusCode: 503);
                var reply = await model.AnswerAsync(request, sources, budget.Token);
                return reply is null ? Results.Json(new { code = "model-busy" }, statusCode: 429) : Results.Ok(reply with {
                    WebStatus = web.Status, WebSources = web.Sources.Select(s => s with { Text = "" }).ToArray()
                });
            }
            catch (HttpRequestException error)
            {
                return Results.Json(new { code = error.StatusCode is null ? "model-connection" : $"model-http-{(int)error.StatusCode}" }, statusCode: 503);
            }
            catch (JsonException)
            {
                return Results.Json(new { code = "invalid-model-reply" }, statusCode: 503);
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or IOException
                or Npgsql.NpgsqlException or InvalidOperationException || error is OperationCanceledException && !http.RequestAborted.IsCancellationRequested)
            {
                // Deliberately no exception/body logging: chat is ephemeral.
                return Results.Json(new { code = "assistant-unavailable" }, statusCode: 503);
            }
        }).RequireRateLimiting("assistant");
    }
}
