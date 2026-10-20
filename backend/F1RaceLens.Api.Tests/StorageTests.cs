using System.Text.Json;
using F1RaceLens.Api.Storage;
using F1RaceLens.Api.Storage.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace F1RaceLens.Api.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RACELENS_TEST_DATABASE")))
            Skip = "Set RACELENS_TEST_DATABASE to an isolated local PostgreSQL database connection.";
    }
}
public sealed partial class StorageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private const string Key = "/api/seasons/1953/races/1/results/race";
    private const string Raw = """
    {"MRData":{"RaceTable":{"Races":[{"season":"1953","round":"1","raceName":"Example Grand Prix","date":"1953-01-18",
    "Circuit":{"circuitId":"example","circuitName":"Example","Location":{"country":"Argentina","locality":"Buenos Aires","lat":"-34.5","long":"-58.5"}},
    "Results":[
    {"number":"1","position":"1","positionText":"1","grid":"2","points":"1.123456789","status":"Finished","Driver":{"driverId":"test","givenName":"Test","familyName":"Driver"},"Constructor":{"constructorId":"first","name":"First"}},
    {"number":"2","position":"10","positionText":"R","grid":"0","points":"-0.5","status":"Engine","Driver":{"driverId":"test","givenName":"Test","familyName":"Driver"},"Constructor":{"constructorId":"second","name":"Second"}}
    ]}]}}}
    """;
    private static string Payload(DateTimeOffset? time = null, int year = 1953) => JsonSerializer.Serialize(new {
        season = year, updatedAt = time ?? Now, isStale = false, source = "Jolpica", entries = new[] { new { points = 1.123456789m } }
    });

    [Fact]
    public void Model_has_reproducible_migrations_and_source_occurrence_keys()
    {
        using var db = new RaceLensDbContext(new DbContextOptionsBuilder<RaceLensDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=design_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var result = db.Model.FindEntityType(typeof(ResultRecord))!;
        Assert.Equal(new[] { "DocumentId", "Ordinal" }, result.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal("numeric", result.FindProperty(nameof(ResultRecord.Points))!.GetColumnType());
        Assert.Equal("text", db.Model.FindEntityType(typeof(SourceDocument))!.FindProperty("Payload")!.GetColumnType());
    }

    [PostgreSqlFact]
    public async Task Real_postgres_preserves_shared_car_transfers_decimals_and_raw_bytes_without_duplicates()
    {
        await using var fixture = await DatabaseFixture.Create();
        var pages = new[] { new CapturedSource("https://api.jolpi.ca/ergast/f1/1953/1/results.json?limit=100", Raw, Now) };
        var payload = Payload();
        await Task.WhenAll(fixture.Store.SaveAsync(Key, payload, pages, default), fixture.Store.SaveAsync(Key, payload, pages, default));
        await fixture.Store.SaveAsync(Key, Payload(Now.AddMinutes(1)), pages, default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Snapshots.CountAsync());
        Assert.Single(await db.Documents.ToListAsync());
        Assert.Equal(Raw, (await db.Documents.SingleAsync()).Payload);
        Assert.Equal(2, await db.Results.CountAsync());
        Assert.Equal(new decimal?[] { 1.123456789m, -0.5m }, await db.Results.OrderBy(x => x.Ordinal).Select(x => x.Points).ToArrayAsync());
        Assert.Equal(new[] { "1", "2" }, await db.Results.OrderBy(x => x.Ordinal).Select(x => x.CarNumber!).ToArrayAsync());
        Assert.Equal(2, await db.Participations.CountAsync());
        Assert.Equal(2, await db.ImportPages.CountAsync());
        Assert.Equal(Now.AddMinutes(1), (await fixture.Store.ReadAsync(Key, 1953, default))!.FetchedAt);
    }

    [PostgreSqlFact]
    public async Task Publication_rolls_back_entirely_when_a_source_page_is_invalid()
    {
        await using var fixture = await DatabaseFixture.Create();
        await fixture.Store.SaveAsync(Key, Payload(), [], default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Store.SaveAsync(Key, Payload(Now.AddMinutes(1)),
            [new CapturedSource("https://api.jolpi.ca/invalid", "{\"invalid\":true}", Now)], default));
        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Snapshots.CountAsync());
        Assert.Equal(0, await db.Documents.CountAsync());
        Assert.Equal(Now, (await fixture.Store.ReadAsync(Key, 1953, default))!.FetchedAt);
    }

    [PostgreSqlFact]
    public async Task Api_reads_database_without_source_and_preserves_stale_timestamp_but_limits_current_fallback()
    {
        await using var fixture = await DatabaseFixture.Create();
        await fixture.Store.SaveAsync(Key, Payload(), [], default);
        var http = new DefaultHttpContext(); http.Request.Method = "GET"; http.Request.Path = Key;
        var clock = new FixedClock(Now);
        var filter = new PersistenceFilter(fixture.Store, clock);
        var invoked = false;
        ValueTask<object?> Next(EndpointFilterInvocationContext _) { invoked = true; return ValueTask.FromResult<object?>(Results.StatusCode(503)); }
        var response = await filter.InvokeAsync(new Invocation(http), Next);
        Assert.False(invoked);
        Assert.Equal(200, ((IStatusCodeHttpResult)response!).StatusCode);
        clock.Now = Now.AddDays(3);
        response = await filter.InvokeAsync(new Invocation(http), Next);
        Assert.True(invoked);
        Assert.Equal(200, ((IStatusCodeHttpResult)response!).StatusCode);
        var value = (System.Text.Json.Nodes.JsonObject)((IValueHttpResult)response!).Value!;
        Assert.True(value["isStale"]!.GetValue<bool>());
        Assert.Equal(Now, value["updatedAt"]!.GetValue<DateTimeOffset>());
        const string current = "/api/seasons/current/standings/drivers";
        await fixture.Store.SaveAsync("/api/seasons/2026/standings/drivers", Payload(Now, 2026), [], default);
        http.Request.Path = current;
        clock.Now = Now; invoked = false;
        response = await filter.InvokeAsync(new Invocation(http), Next);
        Assert.False(invoked);
        Assert.Equal(200, ((IStatusCodeHttpResult)response!).StatusCode);
        clock.Now = Now.AddDays(3);
        response = await filter.InvokeAsync(new Invocation(http), Next);
        Assert.Equal(503, ((IStatusCodeHttpResult)response!).StatusCode);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset Now { get; set; } = now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Invocation(HttpContext http) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext => http;
        public override IList<object?> Arguments => [];
        public override T GetArgument<T>(int index) => throw new ArgumentOutOfRangeException(nameof(index));
    }
    private sealed class DatabaseFixture(string connection, string schema, PooledDbContextFactory<RaceLensDbContext> factory) : IAsyncDisposable
    {
        public IDbContextFactory<RaceLensDbContext> Factory { get; } = factory;
        public SnapshotStore Store { get; } = new(factory);
        public static async Task<DatabaseFixture> Create()
        {
            var connection = Environment.GetEnvironmentVariable("RACELENS_TEST_DATABASE")!;
            var schema = "f1test_" + Guid.NewGuid().ToString("N");
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin); await command.ExecuteNonQueryAsync();
            var settings = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema, Pooling = false };
            var factory = new PooledDbContextFactory<RaceLensDbContext>(new DbContextOptionsBuilder<RaceLensDbContext>().UseNpgsql(settings.ConnectionString).Options);
            await using var db = await factory.CreateDbContextAsync(); await db.Database.MigrateAsync();
            return new DatabaseFixture(connection, schema, factory);
        }
        public async ValueTask DisposeAsync()
        {
            // The identifier is created here, never taken from user input or the application schema.
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin); await command.ExecuteNonQueryAsync();
        }
    }
}
