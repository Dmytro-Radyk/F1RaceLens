using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace F1RaceLens.Api.Accounts;

public sealed record ChangeEmailRequest(string? NewEmail, string? CurrentPassword);
public sealed record ConfirmEmailRequest(string? UserId, string? Token, string? NewEmail = null);

public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/request-email-confirmation", async (HttpContext context, UserManager<ApplicationUser> users, IAccountMailQueue mail) => {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            if (user.EmailConfirmed) return Results.NoContent();
            if (!mail.Available) return Unavailable();
            var message = new AccountMail(AccountMailKind.Confirmation, user.Email!, user.Id,
                await users.GenerateEmailConfirmationTokenAsync(user), user.Language);
            return mail.Enqueue(message) ? Results.Accepted() : Unavailable();
        }).RequireAuthorization().RequireRateLimiting("password-recovery");

        group.MapPost("/request-email-change", async (ChangeEmailRequest request, HttpContext context,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IAccountMailQueue mail) => {
            var email = request.NewEmail?.Trim();
            if (!ValidEmail(email) || request.CurrentPassword is null || request.CurrentPassword.Length is < 1 or > 128)
                return Results.BadRequest(new { code = "invalid-email-change" });
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            if (!(await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true)).Succeeded)
                return Results.BadRequest(new { code = "email-change-failed" });
            if (!mail.Available) return Unavailable();
            if (string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { code = "same-email" });
            // Keep the response uniform for an address owned by another account; do not send it unwanted mail.
            if (await users.FindByEmailAsync(email!) is not null) return Results.Accepted();
            var message = new AccountMail(AccountMailKind.EmailChange, email!, user.Id,
                await users.GenerateChangeEmailTokenAsync(user, email!), user.Language);
            return mail.Enqueue(message) ? Results.Accepted() : Unavailable();
        }).RequireAuthorization().RequireRateLimiting("password-recovery");

        group.MapPost("/confirm-email", async (ConfirmEmailRequest request, UserManager<ApplicationUser> users) => {
            if (!ValidLink(request)) return InvalidLink();
            var user = await users.FindByIdAsync(request.UserId!);
            if (user is null || user.EmailConfirmed) return InvalidLink();
            var result = await users.ConfirmEmailAsync(user, request.Token!);
            return result.Succeeded ? Results.NoContent() : InvalidLink();
        }).RequireRateLimiting("account-entry");

        group.MapPost("/confirm-email-change", async (ConfirmEmailRequest request, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) => {
            if (!ValidLink(request) || !ValidEmail(request.NewEmail)) return InvalidLink();
            var user = await users.FindByIdAsync(request.UserId!);
            if (user is null || string.Equals(user.Email, request.NewEmail, StringComparison.OrdinalIgnoreCase)) return InvalidLink();
            IdentityResult result;
            try { result = await users.ChangeEmailAsync(user, request.NewEmail!, request.Token!); }
            catch (DbUpdateException error) when (error.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return InvalidLink(); }
            if (!result.Succeeded) return InvalidLink();
            // ChangeEmailAsync rotates the stamp and confirms the new address. Login remains the username.
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireRateLimiting("account-entry");
    }

    private static bool ValidEmail(string? email) => email is { Length: > 0 and <= 254 }
        && MailAddress.TryCreate(email, out var address) && address.Address == email;
    private static bool ValidLink(ConfirmEmailRequest request) => request.UserId is { Length: > 0 and <= 128 } && request.Token is { Length: > 0 and <= 4096 };
    private static IResult Unavailable() => Results.Json(new { code = "recovery-unavailable" }, statusCode: 503);
    private static IResult InvalidLink() => Results.BadRequest(new { code = "invalid-email-link" });
}
