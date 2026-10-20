using System.Text.Json;
using F1RaceLens.Api.Storage;
using Microsoft.EntityFrameworkCore;
using F1RaceLens.Api.Calendar;
using F1RaceLens.Api.RaceResults;
using F1RaceLens.Api.Standings;
using F1RaceLens.Api.Drivers;
using F1RaceLens.Api.Teams;
using F1RaceLens.Api.Predictions;
using F1RaceLens.Api.Accounts;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
var connectionString = builder.Configuration.GetConnectionString("RaceLens");
var persistenceEnabled = !string.IsNullOrWhiteSpace(connectionString);
if (!persistenceEnabled && builder.Configuration.GetValue("Storage:Required", true))
    throw new InvalidOperationException("Configure ConnectionStrings:RaceLens or run scripts/database/Setup.ps1. Explicitly set Storage:Required=false only for memory-only development.");
if (persistenceEnabled)
{
    builder.Services.AddPooledDbContextFactory<RaceLensDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddSingleton<SnapshotStore>();
    builder.Services.AddScoped<PersistenceFilter>();
    builder.Services.AddTransient<SourceCaptureHandler>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<F1RaceLens.Api.Predictions.Journal.ForecastJournal>();
    builder.Services.AddScoped(services => services.GetRequiredService<IDbContextFactory<RaceLensDbContext>>().CreateDbContext());
    builder.Services.AddAccounts(builder.Environment);
}
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
var isForecastUpdate = args.Contains("--refresh-data", StringComparer.Ordinal) || args.Contains("--publish-forecasts", StringComparer.Ordinal);
if (isForecastUpdate)
{
    builder.Services.AddSingleton<F1RaceLens.Api.Predictions.Updates.UpdateRequestGate>();
    builder.Services.AddTransient<F1RaceLens.Api.Predictions.Updates.JolpicaUpdateHandler>();
}
var jolpicaClient = builder.Services.AddHttpClient("Jolpica", client =>
{
    client.BaseAddress = new Uri("https://api.jolpi.ca/ergast/f1/");
    client.Timeout = isForecastUpdate ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("F1RaceLens/0.1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
if (persistenceEnabled) jolpicaClient.AddHttpMessageHandler<SourceCaptureHandler>();
if (isForecastUpdate) jolpicaClient.AddHttpMessageHandler<F1RaceLens.Api.Predictions.Updates.JolpicaUpdateHandler>();
builder.Services.AddSingleton<JolpicaCalendarClient>();
builder.Services.AddSingleton<CalendarService>();
builder.Services.AddSingleton<JolpicaResultsClient>();
builder.Services.AddSingleton<ResultsService>();
builder.Services.AddSingleton<JolpicaStandingsClient>();
builder.Services.AddSingleton<StandingsService>();
builder.Services.AddSingleton<JolpicaDriverClient>();
builder.Services.AddSingleton<DriverService>();
builder.Services.AddSingleton<HistoricalTeams>();
builder.Services.AddSingleton<PredictionSource>();
builder.Services.AddHttpClient("OpenMeteo", client => {
    client.BaseAddress = new Uri("https://api.open-meteo.com/");
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddSingleton<ForecastWeatherClient>();
builder.Services.AddSingleton<PredictionService>();
builder.Services.AddSingleton<F1RaceLens.Api.Predictions.Integration.LocalMlForecast>();
builder.Services.AddSingleton<F1RaceLens.Api.Predictions.Updates.ForecastUpdateJob>();
builder.Services.AddSingleton<F1RaceLens.Api.Predictions.Updates.ForecastUpdateStatus>();

var app = builder.Build();
if (args.Contains("--refresh-data", StringComparer.Ordinal) || args.Contains("--publish-forecasts", StringComparer.Ordinal))
{
    if (!persistenceEnabled) throw new InvalidOperationException("Configure PostgreSQL first.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(14));
    var job = app.Services.GetRequiredService<F1RaceLens.Api.Predictions.Updates.ForecastUpdateJob>();
    try
    {
        if (args.Contains("--refresh-data", StringComparer.Ordinal)) await job.RefreshDataAsync(timeout.Token);
        else await job.PublishAsync(timeout.Token);
    }
    catch (F1RaceLens.Api.Predictions.Updates.SourceRateLimitedException)
    {
        app.Logger.LogWarning("Jolpica rate limit reached. Retry after the source cooldown.");
        Environment.ExitCode = 3;
    }
    return;
}
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    if (!persistenceEnabled) throw new InvalidOperationException("Configure ConnectionStrings:RaceLens before migrating.");
    await using var database = await app.Services.GetRequiredService<IDbContextFactory<RaceLensDbContext>>().CreateDbContextAsync();
    await database.Database.MigrateAsync();
    return;
}
app.UseExceptionHandler();
if (persistenceEnabled)
{
    app.Use(async (context, next) => {
        if (context.Request.Path.StartsWithSegments("/api/auth"))
        {
            var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 16 * 1024;
            if (context.Request.ContentLength > 16 * 1024) { context.Response.StatusCode = 413; return; }
        }
        await next(context);
    });
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.MapAccountEndpoints();
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
var sports = app.MapGroup("");
if (persistenceEnabled) sports.AddEndpointFilter<PersistenceFilter>();
sports.MapCalendarEndpoints();
sports.MapResultsEndpoints();
sports.MapStandingsEndpoints();
sports.MapDriverEndpoints();
HistoricalTeams.MapEndpoints(sports);
app.MapGet("/health/storage", async (IServiceProvider services, CancellationToken token) => {
    if (!persistenceEnabled) return Results.Ok(new { status = "disabled" });
    await using var db = await services.GetRequiredService<IDbContextFactory<RaceLensDbContext>>().CreateDbContextAsync(token);
    try {
        if (!await db.Database.CanConnectAsync(token)) return Results.StatusCode(503);
        return (await db.Database.GetPendingMigrationsAsync(token)).Any()
            ? Results.StatusCode(503) : Results.Ok(new { status = "ready" });
    }
    catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException) { return Results.StatusCode(503); }
});
PredictionService.MapEndpoints(app);
app.MapGet("/api/predictions/update-status", async (F1RaceLens.Api.Predictions.Updates.ForecastUpdateStatus status, CancellationToken token)
    => Results.Ok(await status.ReadAsync(token)));
app.MapGet("/api/predictions/ml", async (F1RaceLens.Api.Predictions.Integration.LocalMlForecast model,
    IServiceProvider services, CancellationToken token, ILogger<Program> logger) => {
    try {
        var forecast = await model.ReadAsync(token);
        var journal = services.GetService<F1RaceLens.Api.Predictions.Journal.ForecastJournal>();
        return Results.Ok(journal is null ? forecast : await journal.SaveAsync("ml", forecast, token));
    }
    catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) {
        logger.LogWarning(error, "Local ML forecast unavailable.");
        return Results.Problem(statusCode: 503, title: "ML-прогноз тимчасово недоступний. Потрібен перевірений розрахунок.");
    }
});
app.MapGet("/api/predictions/accuracy", async (IServiceProvider services, ResultsService results, CancellationToken token) => {
    var journal = services.GetService<F1RaceLens.Api.Predictions.Journal.ForecastJournal>();
    return journal is null ? Results.Problem(statusCode: 503, title: "Сховище прогнозів недоступне.")
        : Results.Ok(await journal.AssessAsync(results, token));
});
app.Run();

public partial class Program;
