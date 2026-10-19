using System.Globalization;
using System.Text.Json;

namespace F1RaceLens.Api.Predictions;

public sealed class ForecastWeatherClient(IHttpClientFactory clients, TimeProvider clock, ILogger<ForecastWeatherClient> logger)
{
    public async Task<IReadOnlyList<ForecastEvent>> EnrichAsync(IReadOnlyList<ForecastEvent> events, CancellationToken token)
    {
        var result = new List<ForecastEvent>();
        var forecasts = new Dictionary<(double, double), JsonElement?>();
        foreach (var item in events)
        {
            var status = item.StartsAt is null ? "unknown-time" : item.Latitude is null || item.Longitude is null ? "unknown-location"
                : item.Date < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) || item.Date > DateOnly.FromDateTime(clock.GetUtcNow().AddDays(15).UtcDateTime) ? "outside-horizon" : "forecast";
            if (status != "forecast") { result.Add(item with { Weather = new(null, null, null, status) }); continue; }
            var key = (item.Latitude!.Value, item.Longitude!.Value);
            if (!forecasts.TryGetValue(key, out var forecast))
            {
                try
                {
                    using var client = clients.CreateClient("OpenMeteo");
                    var url = FormattableString.Invariant($"v1/forecast?latitude={key.Item1}&longitude={key.Item2}&hourly=precipitation_probability,temperature_2m,wind_speed_10m&forecast_days=16&timezone=GMT");
                    using var response = await client.GetAsync(url, token); response.EnsureSuccessStatusCode();
                    using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
                    forecast = document.RootElement.Clone();
                }
                catch (Exception error) when (error is HttpRequestException or JsonException || error is OperationCanceledException && !token.IsCancellationRequested)
                { logger.LogWarning(error, "Optional race weather unavailable."); forecast = null; }
                forecasts[key] = forecast;
            }
            result.Add(item with { Weather = forecast is null ? new(null, null, null, "unavailable") : Parse(forecast.Value, item.StartsAt!.Value, clock.GetUtcNow()) });
        }
        return result;
    }
    public static ForecastWeather Parse(JsonElement root, DateTimeOffset startsAt, DateTimeOffset fetchedAt)
    {
        try
        {
            if (root.GetProperty("utc_offset_seconds").GetInt32() != 0) throw new FormatException("Unexpected weather timezone.");
            var hourly = root.GetProperty("hourly");
            var times = hourly.GetProperty("time"); var rain = hourly.GetProperty("precipitation_probability");
            var temperature = hourly.GetProperty("temperature_2m"); var wind = hourly.GetProperty("wind_speed_10m");
            if (new[] { rain, temperature, wind }.Any(values => values.GetArrayLength() != times.GetArrayLength())) throw new FormatException("Truncated weather arrays.");
            var start = new DateTimeOffset(startsAt.UtcDateTime.Year, startsAt.UtcDateTime.Month, startsAt.UtcDateTime.Day, startsAt.UtcDateTime.Hour, 0, 0, TimeSpan.Zero);
            var values = new List<double>(); double? t = null, w = null;
            for (var index = 0; index < times.GetArrayLength(); index++)
            {
                var hour = DateTimeOffset.Parse(times[index].GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                if (hour < start || hour >= start.AddHours(3)) continue;
                var probability = OptionalNumber(rain[index], 0, 100);
                if (probability is not null) values.Add(probability.Value / 100);
                if (hour == start) { t = OptionalNumber(temperature[index], -90, 65); w = OptionalNumber(wind[index], 0, 350); }
            }
            return new(values.Count == 3 ? values.Max() : null, t, w, values.Count == 3 ? "forecast" : "incomplete", fetchedAt);
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { return new(null, null, null, "invalid"); }
    }
    private static double? OptionalNumber(JsonElement value, double minimum, double maximum)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        var number = value.GetDouble();
        if (!double.IsFinite(number) || number < minimum || number > maximum) throw new FormatException("Invalid weather value.");
        return number;
    }
}
