using System.Globalization;
using System.Text.RegularExpressions;
using F1RaceLens.Api.Standings;

namespace F1RaceLens.Api.Drivers;

public static partial class DriverEndpoints
{
    public static void MapDriverEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/seasons/{season}/drivers/{id}", async (string season, string id,
            DriverService profiles, TimeProvider clock, HttpContext context, ILoggerFactory logger) =>
        {
            if (!DriverId().IsMatch(id) || (season != "current" && (season.Length != 4
                || !int.TryParse(season, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                || year < 1950 || year > clock.GetUtcNow().Year + 1)))
                return Results.Problem(statusCode: 400, title: "Некоректний сезон або ідентифікатор пілота.");
            try
            {
                var profile = await profiles.GetAsync(season, id, context.RequestAborted);
                return profile is null ? Results.Problem(statusCode: 404, title: "Пілота не знайдено.") : Results.Ok(profile);
            }
            catch (Exception error) when (error is DriverSourceException or StandingsSourceException)
            {
                logger.CreateLogger("DriverEndpoints").LogWarning(error, "Driver profile request failed.");
                context.Response.Headers.RetryAfter = "60";
                return Results.Problem(statusCode: 503, title: "Профіль пілота тимчасово недоступний.");
            }
        });
    }
    [GeneratedRegex(@"\A[a-z0-9_-]{1,80}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DriverId();
}
