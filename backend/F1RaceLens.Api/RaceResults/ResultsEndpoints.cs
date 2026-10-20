using System.Globalization;
using F1RaceLens.Api.Calendar;

namespace F1RaceLens.Api.RaceResults;

public static class ResultsEndpoints
{
    public static void MapResultsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/seasons/{season}/races/{round:int}/results/{kind}", async (
            string season, int round, string kind, ResultsService results, CalendarService calendar,
            TimeProvider clock, HttpContext context, ILoggerFactory logger) =>
        {
            if (season.Length != 4 || !int.TryParse(season, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                || year < 1950 || year > clock.GetUtcNow().Year + 1 || round is < 1 or > 100
                || kind is not ("race" or "qualifying" or "sprint"))
                return Results.Problem(statusCode: 400, title: "Некоректний сезон, етап або вид сесії.");
            try
            {
                var schedule = await calendar.GetAsync(season, context.RequestAborted);
                if (!schedule.Races.Any(race => race.Round == round))
                    return Results.Problem(statusCode: 404, title: "Гран-прі не знайдено.");
                return Results.Ok(await results.GetAsync(year, round, kind, context.RequestAborted));
            }
            catch (Exception error) when (error is ResultsSourceException or CalendarSourceException)
            {
                logger.CreateLogger("ResultsEndpoints").LogWarning(error, "Results request failed.");
                context.Response.Headers.RetryAfter = "60";
                return Results.Problem(statusCode: 503, title: "Результати тимчасово недоступні.");
            }
        });
    }
}
