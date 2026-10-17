using F1RaceLens.Api.Calendar;

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

var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapCalendarEndpoints();
app.Run();

public partial class Program;
