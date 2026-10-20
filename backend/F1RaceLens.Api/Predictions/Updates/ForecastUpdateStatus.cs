using System.Text.Json;

namespace F1RaceLens.Api.Predictions.Updates;

public sealed record UpdateStatus(string State, string? Stage = null, DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null, DateTimeOffset? LastSuccessfulAt = null, string? Error = null);

public sealed class ForecastUpdateStatus(IHostEnvironment environment, TimeProvider clock)
{
    public async Task<UpdateStatus> ReadAsync(CancellationToken token)
    {
        var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../../.local/forecast-update.json"));
        if (!File.Exists(path)) return new("never");
        try
        {
            if (new FileInfo(path).Length > 65536) return new("unavailable");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = await JsonDocument.ParseAsync(file, cancellationToken: token);
            return Parse(json.RootElement, clock.GetUtcNow());
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return new("unavailable"); }
    }

    public static UpdateStatus Parse(JsonElement data, DateTimeOffset now)
    {
        string? Text(string key) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var field)
            && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        DateTimeOffset? Date(string key) => DateTimeOffset.TryParse(Text(key), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var time) && time <= now ? time.ToUniversalTime() : null;
        var state = Text("state"); var stage = Text("stage");
        if (state is not ("running" or "succeeded" or "failed")) return new("unavailable");
        if (stage is not ("preflight" or "data" or "ml" or "publish" or "complete")) stage = null;
        if (state == "running" && (Date("updatedAt") is not { } heartbeat || heartbeat.AddHours(1) < now)) state = "interrupted";
        return new(state, stage, Date("startedAt"), Date("finishedAt"), Date("lastSuccessfulAt"),
            state is "failed" or "interrupted" ? Text("error") == "source-busy" ? "source-busy" : "update-failed" : null);
    }
}
