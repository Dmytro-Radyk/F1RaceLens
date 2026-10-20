using System.Globalization;

namespace F1RaceLens.Api.Standings;

public static class StandingsEndpoints
{
    public static void MapStandingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/seasons/{season}/standings/{kind}", async (
            string season, string kind, StandingsService standings, TimeProvider clock,
            HttpContext context, ILoggerFactory logger) =>
        {
            if ((season != "current" && (season.Length != 4
                || !int.TryParse(season, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                || year < 1950 || year > clock.GetUtcNow().Year + 1)) || kind is not ("drivers" or "constructors"))
                return Results.Problem(statusCode: 400, title: "Некоректний сезон або вид заліку.");
            try { return Results.Ok(await standings.GetAsync(season, kind, context.RequestAborted)); }
            catch (StandingsSourceException error)
            {
                logger.CreateLogger("StandingsEndpoints").LogWarning(error, "Standings request failed.");
                context.Response.Headers.RetryAfter = "60";
                return Results.Problem(statusCode: 503, title: "Турнірна таблиця тимчасово недоступна.");
            }
        });
    }
}
