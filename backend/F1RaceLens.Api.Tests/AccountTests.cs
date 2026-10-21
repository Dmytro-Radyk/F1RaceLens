using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using F1RaceLens.Api.Accounts;
using F1RaceLens.Api.Storage;
using F1RaceLens.Api.Storage.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace F1RaceLens.Api.Tests;

public partial class StorageTests
{
    private const string TestPassword = "A test password for accounts 2026";
    private sealed class TestAccountMailQueue(bool available) : IAccountMailQueue
    {
        public bool Available => available;
        public List<AccountMail> Messages { get; } = [];
        public bool Enqueue(AccountMail mail) { Messages.Add(mail); return true; }
    }
    private sealed class AccountApplication(DatabaseFixture fixture, bool mailAvailable = false) : WebApplicationFactory<Program>
    {
        public TestAccountMailQueue Mail { get; } = new(mailAvailable);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => {
                services.RemoveAll<IDbContextFactory<RaceLensDbContext>>();
                services.AddSingleton(fixture.Factory);
                services.RemoveAll<RaceLensDbContext>();
                services.AddScoped(_ => fixture.Factory.CreateDbContext());
                services.RemoveAll<IAccountMailQueue>();
                services.AddSingleton<IAccountMailQueue>(Mail);
            });
        }
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
    }
    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        var value = Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", value);
    }
    private static async Task<AccountResponse> Register(HttpClient client, string login = "first-user", string language = "uk")
    {
        await Csrf(client);
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(login, login + "@example.test", TestPassword, language, "dark"));
        response.EnsureSuccessStatusCode();
        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("F1RaceLens.Session=") && value.Contains("httponly", StringComparison.OrdinalIgnoreCase) && value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        await Csrf(client);
        return account;
    }

    [PostgreSqlFact]
    public async Task Identity_registers_hashes_password_restores_session_and_logs_out_on_server()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var client = application.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var account = await Register(client);
        await using (var db = await fixture.Factory.CreateDbContextAsync()) {
            var stored = await db.Users.SingleAsync();
            Assert.NotEqual(TestPassword, stored.PasswordHash);
            Assert.StartsWith("AQAAAA", stored.PasswordHash);
            Assert.False(stored.EmailConfirmed);
        }
        Assert.Equal(account.Id, (await client.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new {})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        await Csrf(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("FIRST-USER", TestPassword));
        login.EnsureSuccessStatusCode();
        Assert.Equal(account.Id, (await login.Content.ReadFromJsonAsync<AccountResponse>())!.Id);
    }

    [PostgreSqlFact]
    public async Task Account_mutations_require_identity_bound_csrf_and_guest_cannot_read_preferences()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var guest = application.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "12345"))).StatusCode);
        var account = await Register(guest);
        guest.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PutAsJsonAsync("/api/auth/preferences", new PreferencesRequest("de", "light"))).StatusCode);
        Assert.Equal("uk", (await guest.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!.Language);
        using var stranger = application.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.PutAsJsonAsync("/api/auth/favourites", new FavouritesRequest([], []))).StatusCode);
        await Csrf(stranger);
        var strangerToken = stranger.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN").Single();
        guest.DefaultRequestHeaders.Add("X-XSRF-TOKEN", strangerToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/auth/logout", new {})).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Favourites_and_preferences_are_persistent_isolated_and_validated()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using (var db = await fixture.Factory.CreateDbContextAsync()) {
            db.Drivers.Add(new DriverRecord { Id = "test_driver", Name = "Test Driver" });
            db.Constructors.Add(new ConstructorRecord { Id = "test_team", Name = "Test Team" });
            await db.SaveChangesAsync();
        }
        await using var application = new AccountApplication(fixture);
        using var first = application.Client(); using var second = application.Client();
        var one = await Register(first); await Register(second, "second-user", "fr");
        (await first.PutAsJsonAsync("/api/auth/preferences", new { language = "de", theme = "light", userId = "second-user" })).EnsureSuccessStatusCode();
        (await first.PutAsJsonAsync("/api/auth/favourites", new FavouritesRequest(["test_driver", "test_driver"], ["test_team"]))).EnsureSuccessStatusCode();
        var saved = (await first.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!;
        Assert.Equal("de", saved.Language); Assert.Equal("light", saved.Theme);
        Assert.Equal(new[] { "test_driver" }, saved.DriverIds);
        Assert.Contains(saved.Favourites, row => row.Name == "Test Driver");
        var other = (await second.GetFromJsonAsync<AccountResponse>("/api/auth/me"))!;
        Assert.Equal("fr", other.Language); Assert.Empty(other.DriverIds); Assert.Empty(other.TeamIds);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PutAsJsonAsync("/api/auth/preferences", new PreferencesRequest("xx", "dark"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PutAsJsonAsync("/api/auth/favourites", new FavouritesRequest(["unknown"], []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PutAsJsonAsync("/api/auth/favourites", new FavouritesRequest(Enumerable.Repeat("test_driver",21).ToArray(), []))).StatusCode);
        (await first.PutAsJsonAsync("/api/auth/favourites", new FavouritesRequest([], []))).EnsureSuccessStatusCode();
        await using var verify = await fixture.Factory.CreateDbContextAsync();
        Assert.Equal("de", (await verify.Users.SingleAsync(user => user.Id == one.Id)).Language);
        Assert.Empty(await verify.Set<UserFavourite>().ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task Registration_validation_duplicates_and_lockout_do_not_create_demo_access()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var client = application.Client();
        await Csrf(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("admin", "admin@example.test", "12345", "uk", "dark"))).StatusCode);
        await Register(client);
        (await client.PostAsJsonAsync("/api/auth/logout", new {})).EnsureSuccessStatusCode(); await Csrf(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("first-user", "different@example.test", TestPassword, "uk", "dark"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("another-user", "FIRST-USER@example.test", TestPassword, "uk", "dark"))).StatusCode);
        for (var attempt=0; attempt<5; attempt++) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("first-user", "wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("first-user", TestPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "12345"))).StatusCode);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Single(await db.Users.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task Account_endpoints_reject_excessive_attempts_and_large_bodies()
    {
        await using var fixture = await DatabaseFixture.Create();
        await using var application = new AccountApplication(fixture);
        using var client = application.Client(); await Csrf(client);
        for (var attempt=0; attempt<20; attempt++) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("absent-user", "wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("absent-user", "wrong"))).StatusCode);
        using var large = new StringContent(JsonSerializer.Serialize(new LoginRequest("absent-user", new string('x', 17000))), System.Text.Encoding.UTF8, "application/json");
        large.Headers.ContentLength = System.Text.Encoding.UTF8.GetByteCount(await large.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/auth/login", large)).StatusCode);
    }
}
