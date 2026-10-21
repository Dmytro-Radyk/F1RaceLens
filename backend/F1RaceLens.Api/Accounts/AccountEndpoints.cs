using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using F1RaceLens.Api.Storage;

namespace F1RaceLens.Api.Accounts;

public static partial class AccountEndpoints
{
    private static readonly string[] Languages = ["uk", "en", "de", "it", "es", "fr"];
    [GeneratedRegex("^[a-zA-Z0-9_-]{3,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex LoginPattern();
    [GeneratedRegex("^[a-z0-9_-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityPattern();
    public static bool ValidPreferences(string? language, string? theme) => Languages.Contains(language) && theme is "dark" or "light";

    public static void MapAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").AddEndpointFilter<AccountCsrfFilter>();
        group.MapPasswordEndpoints();
        group.MapEmailEndpoints();
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) => {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions {
                HttpOnly = false, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/"
            });
            return Results.NoContent();
        });
        group.MapPost("/register", Register).RequireRateLimiting("account-entry");
        group.MapPost("/login", Login).RequireRateLimiting("account-entry");
        group.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> users, RaceLensDbContext db, CancellationToken token) => {
            var user = await users.GetUserAsync(context.User);
            return user is null ? Results.Unauthorized() : Results.Ok(await Response(user, db, token));
        }).RequireAuthorization();
        group.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) => {
            await signIn.SignOutAsync(); return Results.NoContent();
        }).RequireAuthorization();
        group.MapPut("/preferences", async (PreferencesRequest request, HttpContext context, UserManager<ApplicationUser> users, RaceLensDbContext db, CancellationToken token) => {
            if (!ValidPreferences(request.Language, request.Theme)) return Results.BadRequest(new { code = "invalid-preferences" });
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            user.Language = request.Language!; user.Theme = request.Theme!;
            var saved = await users.UpdateAsync(user);
            return saved.Succeeded ? Results.Ok(await Response(user, db, token)) : Results.Conflict(new { code = "save-conflict" });
        }).RequireAuthorization();
        group.MapPut("/favourites", Favourites).RequireAuthorization();
    }

    private static async Task<IResult> Register(RegisterRequest request, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, RaceLensDbContext db, IAccountMailQueue mail, CancellationToken token)
    {
        var login = request.Login?.Trim(); var email = request.Email?.Trim();
        if (login is null || !LoginPattern().IsMatch(login) || email is null || email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email
            || request.Password is null || request.Password.Length is < 12 or > 128 || !ValidPreferences(request.Language, request.Theme))
            return Results.BadRequest(new { code = "invalid-registration" });
        var user = new ApplicationUser { UserName = login, Email = email, Language = request.Language!, Theme = request.Theme! };
        IdentityResult created;
        try { created = await users.CreateAsync(user, request.Password); }
        catch (DbUpdateException error) when (error.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Results.BadRequest(new { code = "registration-unavailable" }); }
        if (!created.Succeeded) return Results.BadRequest(new { code = "registration-unavailable" });
        await signIn.SignInAsync(user, isPersistent: false);
        if (mail.Available) mail.Enqueue(new AccountMail(AccountMailKind.Confirmation, user.Email!, user.Id, await users.GenerateEmailConfirmationTokenAsync(user), user.Language));
        return Results.Ok(await Response(user, db, token));
    }
    private static async Task<IResult> Login(LoginRequest request, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, RaceLensDbContext db, CancellationToken token)
    {
        if (request.Login is null || request.Login.Length > 254 || request.Password is null || request.Password.Length is < 1 or > 128)
            return Results.Unauthorized();
        var user = await users.FindByNameAsync(request.Login.Trim());
        if (user is null)
        {
            // Match a password verification cost without authenticating or storing this user.
            users.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyHash, request.Password);
            return Results.Unauthorized();
        }
        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded ? Results.Ok(await Response(user, db, token)) : Results.Unauthorized();
    }
    private static readonly string DummyHash = new PasswordHasher<ApplicationUser>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }))
        .HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N"));

    private static async Task<IResult> Favourites(FavouritesRequest request, HttpContext context, UserManager<ApplicationUser> users, RaceLensDbContext db, CancellationToken token)
    {
        var drivers = request.DriverIds; var teams = request.TeamIds;
        if (drivers is null || teams is null || drivers.Length > 20 || teams.Length > 20 || drivers.Concat(teams).Any(id => id is null || !EntityPattern().IsMatch(id)))
            return Results.BadRequest(new { code = "invalid-favourites" });
        drivers = drivers.Distinct().ToArray(); teams = teams.Distinct().ToArray();
        if (await db.Drivers.CountAsync(x => drivers.Contains(x.Id), token) != drivers.Length || await db.Constructors.CountAsync(x => teams.Contains(x.Id), token) != teams.Length)
            return Results.BadRequest(new { code = "unknown-favourite" });
        var user = await users.GetUserAsync(context.User);
        if (user is null) return Results.Unauthorized();
        var existing = await db.Set<UserFavourite>().Where(x => x.UserId == user.Id).ToListAsync(token);
        var desired = drivers.Select(id => (Kind: "driver", Id: id)).Concat(teams.Select(id => (Kind: "team", Id: id))).ToHashSet();
        db.RemoveRange(existing.Where(x => !desired.Contains((x.Kind, x.EntityId))));
        foreach (var item in desired.Where(item => !existing.Any(x => x.Kind == item.Kind && x.EntityId == item.Id)))
            db.Add(new UserFavourite { UserId = user.Id, Kind = item.Kind, EntityId = item.Id });
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException) { return Results.Conflict(new { code = "save-conflict" }); }
        return Results.Ok(await Response(user, db, token));
    }
    private static async Task<AccountResponse> Response(ApplicationUser user, RaceLensDbContext db, CancellationToken token)
    {
        var favourites = await db.Set<UserFavourite>().AsNoTracking().Where(x => x.UserId == user.Id).OrderBy(x => x.EntityId).ToArrayAsync(token);
        var driverIds = favourites.Where(x => x.Kind == "driver").Select(x => x.EntityId).ToArray();
        var teamIds = favourites.Where(x => x.Kind == "team").Select(x => x.EntityId).ToArray();
        var driverNames = await db.Drivers.AsNoTracking().Where(x => driverIds.Contains(x.Id)).Select(x => new FavouriteResponse("driver", x.Id, x.Name)).ToArrayAsync(token);
        var teamNames = await db.Constructors.AsNoTracking().Where(x => teamIds.Contains(x.Id)).Select(x => new FavouriteResponse("team", x.Id, x.Name)).ToArrayAsync(token);
        return new(user.Id, user.UserName!, user.Email!, user.Language, user.Theme,
            driverIds, teamIds, [.. driverNames, .. teamNames], user.EmailConfirmed);
    }
}
