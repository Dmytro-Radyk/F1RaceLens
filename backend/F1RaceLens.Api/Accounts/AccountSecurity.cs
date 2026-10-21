using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using F1RaceLens.Api.Storage;

namespace F1RaceLens.Api.Accounts;

public static class AccountSecurity
{
    public static void AddAccounts(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddIdentityCore<ApplicationUser>(options => {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        }).AddEntityFrameworkStores<RaceLensDbContext>().AddSignInManager().AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromMinutes(30));
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.AddSingleton<PasswordRecovery>();
        services.AddSingleton<IAccountMailQueue>(services => services.GetRequiredService<PasswordRecovery>());
        services.AddSingleton<IAccountMailTransport, SmtpAccountMailTransport>();
        services.AddHostedService(services => services.GetRequiredService<PasswordRecovery>());
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => {
            options.Cookie.Name = "F1RaceLens.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddAuthorization();
        services.AddAntiforgery(options => {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "F1RaceLens.Antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        var keys = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../../.local/identity-keys"));
        var protection = services.AddDataProtection().SetApplicationName("F1RaceLens").PersistKeysToFileSystem(new DirectoryInfo(keys));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        services.AddRateLimiter(options => {
            options.RejectionStatusCode = 429;
            options.AddPolicy("password-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
            options.AddPolicy("account-entry", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }
}

public sealed class AccountCsrfFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { code = "csrf" }); }
        }
        return await next(context);
    }
}
