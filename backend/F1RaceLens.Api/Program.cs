using F1RaceLens.Api.Calendar;
using F1RaceLens.Api.RaceResults;
using F1RaceLens.Api.Standings;
using F1RaceLens.Api.Drivers;
using F1RaceLens.Api.Teams;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient("Jolpica", client =>
{
    client.BaseAddress = new Uri("https://api.jolpi.ca/ergast/f1/");
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("F1RaceLens/0.1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddSingleton<JolpicaCalendarClient>();
builder.Services.AddSingleton<CalendarService>();
builder.Services.AddSingleton<JolpicaResultsClient>();
builder.Services.AddSingleton<ResultsService>();
builder.Services.AddSingleton<JolpicaStandingsClient>();
builder.Services.AddSingleton<StandingsService>();
builder.Services.AddSingleton<JolpicaDriverClient>();
builder.Services.AddSingleton<DriverService>();
builder.Services.AddSingleton<HistoricalTeams>();

var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapCalendarEndpoints();
app.MapResultsEndpoints();
app.MapStandingsEndpoints();
app.MapDriverEndpoints();
HistoricalTeams.MapEndpoints(app);
app.Run();

public partial class Program;
