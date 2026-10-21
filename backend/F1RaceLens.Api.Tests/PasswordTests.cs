using System.Net;
using System.Net.Http.Json;
using F1RaceLens.Api.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace F1RaceLens.Api.Tests;

public partial class StorageTests
{
    private const string ChangedPassword = "A different test password 2026";
    private sealed class MailTestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "MailTests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("http://127.0.0.1:4200", true)]
    [InlineData("http://example.test", false)]
    [InlineData("http://localhost.example.test", false)]
    public void Development_http_email_links_are_allowed_only_for_loopback(string origin, bool available)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["PasswordRecovery:PublicOrigin"] = origin, ["PasswordRecovery:SmtpHost"] = "smtp.example.test",
            ["PasswordRecovery:From"] = "service@example.test", ["PasswordRecovery:Username"] = "test-only",
            ["PasswordRecovery:Password"] = "test-only-not-a-real-secret"
        }).Build();
        using var recovery = new PasswordRecovery(services.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<PasswordRecovery>.Instance, new MailTestEnvironment());
        Assert.Equal(available, recovery.Available);
    }

    [Theory]
    [InlineData("http://example.test", false)]
    [InlineData("https://example.test/path", false)]
    [InlineData("https://example.test?anything=1", false)]
    [InlineData("https://user@example.test", false)]
    [InlineData("https://example.test", true)]
    public void Recovery_requires_fixed_https_origin_and_bounds_its_queue(string origin, bool available)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["PasswordRecovery:PublicOrigin"] = origin, ["PasswordRecovery:SmtpHost"] = "smtp.example.test",
            ["PasswordRecovery:From"] = "service@example.test", ["PasswordRecovery:Username"] = "test-only",
            ["PasswordRecovery:Password"] = "test-only-not-a-real-secret"
        }).Build();
        using var recovery = new PasswordRecovery(services.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<PasswordRecovery>.Instance);
        Assert.Equal(available, recovery.Available);
        for (var request = 0; request < 64; request++) Assert.True(recovery.Enqueue("recipient@example.test"));
        Assert.False(recovery.Enqueue("recipient@example.test"));
    }

    [PostgreSqlFact]
    public async Task Password_change_checks_old_password_and_revokes_other_sessions_only()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var first = application.Client(); using var other = application.Client();
        await Register(first);
        await Csrf(other);
        (await other.PostAsJsonAsync("/api/auth/login", new LoginRequest("first-user", TestPassword))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("wrong", ChangedPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(TestPassword, "short"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(TestPassword, TestPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(TestPassword, ChangedPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me")).StatusCode);
        await Csrf(other);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync("/api/auth/login", new LoginRequest("first-user", TestPassword))).StatusCode);
        (await other.PostAsJsonAsync("/api/auth/login", new LoginRequest("first-user", ChangedPassword))).EnsureSuccessStatusCode();
    }

    [PostgreSqlFact]
    public async Task Password_reset_is_single_use_user_bound_and_revokes_sessions()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var first = application.Client(); using var second = application.Client(); using var guest = application.Client();
        var account = await Register(first);
        var another = await Register(second, "second-user");
        using var scope = application.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(account.Id))!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await Csrf(guest);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(another.Id, token, ChangedPassword))).StatusCode);
        var request = new ResetPasswordRequest(account.Id, token, ChangedPassword);
        Assert.Equal(HttpStatusCode.NoContent, (await guest.PostAsJsonAsync("/api/auth/reset-password", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/auth/reset-password", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/me")).StatusCode);
        (await guest.PostAsJsonAsync("/api/auth/login", new LoginRequest("first-user", ChangedPassword))).EnsureSuccessStatusCode();
    }

    [PostgreSqlFact]
    public async Task Reset_tokens_expire_and_password_changes_invalidate_outstanding_links()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var client = application.Client(); var account = await Register(client);
        using var scope = application.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(30), options.TokenLifespan);
        var user = (await users.FindByIdAsync(account.Id))!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        options.TokenLifespan = TimeSpan.FromSeconds(-1);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(account.Id, token, ChangedPassword))).StatusCode);
        options.TokenLifespan = TimeSpan.FromMinutes(30);
        (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(TestPassword, ChangedPassword))).EnsureSuccessStatusCode();
        await Csrf(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(account.Id, token, TestPassword))).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Recovery_without_smtp_is_honest_uniform_rate_limited_and_csrf_protected()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var client = application.Client(); await Register(client);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("first-user@example.test"))).StatusCode);
        await Csrf(client);
        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("first-user@example.test"));
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("absent@example.test"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("absent@example.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("absent@example.test"))).StatusCode);
        using var guest = application.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(TestPassword, ChangedPassword))).StatusCode);
        await Csrf(guest);
        guest.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest("any", "any", ChangedPassword))).StatusCode);
    }
}
