using System.Net;
using System.Net.Http.Json;
using F1RaceLens.Api.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1RaceLens.Api.Tests;

public partial class StorageTests
{
    private sealed class CaptureMailTransport : IAccountMailTransport
    {
        public TaskCompletionSource<(string Recipient, string Subject, string Body)> Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        { Delivered.TrySetResult((recipient, subject, body)); return Task.CompletedTask; }
    }

    [PostgreSqlFact]
    public async Task Mail_worker_creates_localized_fragment_links_without_contacting_smtp()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var client = application.Client(); var account = await Register(client, language: "fr");
        using var scope = application.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(account.Id))!;
        foreach (var (kind, expectedPath) in new[] {
            (AccountMailKind.Recovery, "/reset-password"), (AccountMailKind.Confirmation, "/confirm-email"), (AccountMailKind.EmailChange, "/confirm-email-change") }) {
        var recipient = kind == AccountMailKind.EmailChange ? "new@example.test" : account.Email;
        var token = kind == AccountMailKind.EmailChange ? await users.GenerateChangeEmailTokenAsync(user, recipient)
            : await users.GenerateEmailConfirmationTokenAsync(user);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["PasswordRecovery:PublicOrigin"] = "https://example.test", ["PasswordRecovery:SmtpHost"] = "smtp.example.test",
            ["PasswordRecovery:From"] = "sender@example.test", ["PasswordRecovery:Username"] = "test-only",
            ["PasswordRecovery:Password"] = "test-only-not-a-real-secret"
        }).Build();
        var transport = new CaptureMailTransport();
        using var worker = new PasswordRecovery(application.Services.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<PasswordRecovery>.Instance, transport: transport);
        Assert.True(worker.Available);
        await worker.StartAsync(CancellationToken.None);
        try {
            Assert.True(worker.Enqueue(new AccountMail(kind, recipient, account.Id, kind == AccountMailKind.Recovery ? null : token)));
            var sent = await transport.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(recipient, sent.Recipient);
            Assert.True(sent.Body.Contains("30 minutes", StringComparison.Ordinal));
            var link = new Uri(sent.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last());
            Assert.Equal(expectedPath, link.AbsolutePath); Assert.Empty(link.Query);
            // Report only a boolean on failure, never the token-bearing fragment.
            var hasUserFragment = link.Fragment.StartsWith("#user=", StringComparison.Ordinal);
            Assert.True(hasUserFragment);
        } finally { await worker.StopAsync(CancellationToken.None); }
        }
    }
    [PostgreSqlFact]
    public async Task Email_confirmation_is_explicit_single_use_user_and_purpose_bound()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture, mailAvailable: true);
        using var first = application.Client(); using var other = application.Client();
        var account = await Register(first); var second = await Register(other, "second-user");
        Assert.False(account.EmailConfirmed);
        var mail = application.Mail.Messages.Single(x => x.UserId == account.Id);
        Assert.Equal(AccountMailKind.Confirmation, mail.Kind);
        first.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(account.Id, mail.Token))).StatusCode);
        await Csrf(first);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(second.Id, mail.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(account.Id, mail.Token, ChangedPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(account.Id, mail.Token))).StatusCode);
        Assert.True((await first.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.EmailConfirmed);
        Assert.False((await other.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.EmailConfirmed);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(account.Id, mail.Token))).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Email_change_keeps_old_address_until_verified_then_revokes_sessions_and_preserves_login()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture, mailAvailable: true);
        using var first = application.Client(); using var anotherSession = application.Client();
        var account = await Register(first);
        await Csrf(anotherSession);
        (await anotherSession.PostAsJsonAsync("/api/auth/login", new LoginRequest(account.Login, TestPassword))).EnsureSuccessStatusCode();
        var before = application.Mail.Messages.Count;
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/request-email-change", new ChangeEmailRequest("new@example.test", "wrong"))).StatusCode);
        Assert.Equal(before, application.Mail.Messages.Count);
        Assert.Equal(HttpStatusCode.Accepted, (await first.PostAsJsonAsync("/api/auth/request-email-change", new ChangeEmailRequest("new@example.test", TestPassword))).StatusCode);
        Assert.Equal(account.Email, (await first.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.Email);
        var mail = application.Mail.Messages.Last();
        Assert.Equal(AccountMailKind.EmailChange, mail.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/confirm-email-change", new ConfirmEmailRequest(account.Id, mail.Token, "tampered@example.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsJsonAsync("/api/auth/confirm-email-change", new ConfirmEmailRequest(account.Id, mail.Token, "new@example.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anotherSession.GetAsync("/api/auth/me")).StatusCode);
        await Csrf(first);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/confirm-email-change", new ConfirmEmailRequest(account.Id, mail.Token, "new@example.test"))).StatusCode);
        (await first.PostAsJsonAsync("/api/auth/login", new LoginRequest(account.Login, TestPassword))).EnsureSuccessStatusCode();
        var updated = (await first.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!;
        Assert.Equal(account.Id, updated.Id); Assert.Equal(account.Login, updated.Login);
        Assert.Equal("new@example.test", updated.Email); Assert.True(updated.EmailConfirmed);
    }

    [PostgreSqlFact]
    public async Task Email_tokens_expire_and_duplicates_or_disabled_delivery_never_replace_an_address()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture, mailAvailable: true);
        using var first = application.Client(); using var second = application.Client();
        var account = await Register(first); await Register(second, "second-user");
        var count = application.Mail.Messages.Count;
        Assert.Equal(HttpStatusCode.Accepted, (await first.PostAsJsonAsync("/api/auth/request-email-change", new ChangeEmailRequest("SECOND-USER@example.test", TestPassword))).StatusCode);
        Assert.Equal(count, application.Mail.Messages.Count);
        using var scope = application.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(account.Id))!;
        var changeToken = await users.GenerateChangeEmailTokenAsync(user, "second-user@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/confirm-email-change", new ConfirmEmailRequest(account.Id, changeToken, "second-user@example.test"))).StatusCode);
        var confirmation = application.Mail.Messages.First(x => x.UserId == account.Id);
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value;
        options.TokenLifespan = TimeSpan.FromSeconds(-1);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(account.Id, confirmation.Token))).StatusCode);
        Assert.Equal(account.Email, (await first.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.Email);
        Assert.False((await first.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.EmailConfirmed);
    }

    [PostgreSqlFact]
    public async Task Email_requests_without_delivery_are_unavailable_and_require_authorization()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var client = application.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/request-email-confirmation", new {})).StatusCode);
        var account = await Register(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/request-email-confirmation", new {})).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/request-email-change", new ChangeEmailRequest("new@example.test", TestPassword))).StatusCode);
        Assert.Equal(account.Email, (await client.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.Email);
        Assert.Empty(application.Mail.Messages);
    }
}
