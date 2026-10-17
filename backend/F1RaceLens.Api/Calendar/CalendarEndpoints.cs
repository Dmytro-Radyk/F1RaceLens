using System.Globalization;

namespace F1RaceLens.Api.Calendar;

public static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this WebApplication app)
    {
        app.MapGet("/api/seasons/{season}/calendar", async (
            string season, CalendarService calendar, TimeProvider clock, HttpContext context, ILoggerFactory logger) =>
        {
            if (!ValidSeason(season, clock)) return InvalidRequest();
            try
            {
                return Results.Ok(await calendar.GetAsync(season, context.RequestAborted));
            }
            catch (CalendarSourceException error)
            {
                return Unavailable(context, logger, error);
            }
        });

        app.MapGet("/api/seasons/{season}/races/{round:int}", async (
            string season, int round, CalendarService calendar, TimeProvider clock, HttpContext context, ILoggerFactory logger) =>
        {
            if (season == "current" || !ValidSeason(season, clock) || round is < 1 or > 100) return InvalidRequest();
            try
            {
                var data = await calendar.GetAsync(season, context.RequestAborted);
                var race = data.Races.SingleOrDefault(item => item.Round == round);
                return race is null
                    ? Results.Problem(statusCode: 404, title: "Гран-прі не знайдено.")
                    : Results.Ok(new RaceResponse(data.Season, data.UpdatedAt, data.IsStale, data.Source, race));
            }
            catch (CalendarSourceException error)
            {
                return Unavailable(context, logger, error);
            }
        });
    }

    private static bool ValidSeason(string season, TimeProvider clock) => season == "current" ||
        (season.Length == 4 && int.TryParse(season, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && year >= 1950 && year <= clock.GetUtcNow().Year + 1);

    private static IResult InvalidRequest() => Results.Problem(statusCode: 400, title: "Некоректний сезон або номер етапу.");

    private static IResult Unavailable(HttpContext context, ILoggerFactory logger, Exception error)
    {
        logger.CreateLogger("CalendarEndpoints").LogWarning(error, "Calendar request failed.");
        context.Response.Headers.RetryAfter = "60";
        return Results.Problem(statusCode: 503, title: "Календар тимчасово недоступний.",
            detail: "Спробуйте завантажити дані трохи пізніше.");
    }
}
