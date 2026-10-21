using System.Net.Mail;
using Microsoft.AspNetCore.Identity;

namespace F1RaceLens.Api.Accounts;

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record ForgotPasswordRequest(string? Email);
public sealed record ResetPasswordRequest(string? UserId, string? Token, string? NewPassword);

public static class PasswordEndpoints
{
    public static void MapPasswordEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/change-password", async (ChangePasswordRequest request, HttpContext context,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) => {
            if (!ValidPassword(request.NewPassword) || request.CurrentPassword is null || request.CurrentPassword.Length is < 1 or > 128)
                return Results.BadRequest(new { code = "invalid-password" });
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            // Use Identity's lockout counter as well as the IP request limiter.
            var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
            if (!check.Succeeded) return Results.BadRequest(new { code = "password-change-failed" });
            if (request.NewPassword == request.CurrentPassword)
                return Results.BadRequest(new { code = "same-password" });
            var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword!);
            if (!changed.Succeeded) return Results.BadRequest(new { code = "password-change-failed" });
            await signIn.RefreshSignInAsync(user);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("account-entry");

        group.MapPost("/forgot-password", (ForgotPasswordRequest request, IAccountMailQueue recovery) => {
            // Availability and validation never depend on whether an account exists.
            if (!recovery.Available) return Results.Json(new { code = "recovery-unavailable" }, statusCode: 503);
            var email = request.Email?.Trim();
            if (email is null || email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
                return Results.BadRequest(new { code = "invalid-email" });
            return recovery.Enqueue(new AccountMail(AccountMailKind.Recovery, email)) ? Results.Accepted() : Results.Json(new { code = "recovery-unavailable" }, statusCode: 503);
        }).RequireRateLimiting("password-recovery");

        group.MapPost("/reset-password", async (ResetPasswordRequest request, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) => {
            if (!ValidPassword(request.NewPassword)) return Results.BadRequest(new { code = "invalid-password" });
            if (request.UserId is null || request.UserId.Length > 128 || request.Token is null || request.Token.Length is < 1 or > 4096)
                return Results.BadRequest(new { code = "invalid-reset" });
            var user = await users.FindByIdAsync(request.UserId);
            if (user is null) return Results.BadRequest(new { code = "invalid-reset" });
            var reset = await users.ResetPasswordAsync(user, request.Token, request.NewPassword!);
            if (reset.Succeeded) await signIn.SignOutAsync();
            // Identity rotates the security stamp: existing cookies and this token become invalid.
            return reset.Succeeded ? Results.NoContent() : Results.BadRequest(new { code = "invalid-reset" });
        }).RequireRateLimiting("account-entry");
    }

    private static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 128 };
}
