using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using F1RaceLens.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1RaceLens.Api.Storage;

public sealed class SnapshotStore(IDbContextFactory<RaceLensDbContext> factory)
{
    public async Task<ResourceSnapshot?> ReadAsync(string key, int? expectedSeason, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.Snapshots.AsNoTracking().Where(row => row.ResourceKey == key && (expectedSeason == null || row.Season == expectedSeason))
            .OrderByDescending(row => row.FetchedAt).ThenByDescending(row => row.Id).FirstOrDefaultAsync(token);
    }
    public async Task SaveAsync(string key, string payload, IReadOnlyCollection<CapturedSource> pages, CancellationToken token)
    {
        using var json = JsonDocument.Parse(payload);
        var data = json.RootElement;
        if (data.GetProperty("isStale").GetBoolean()) return;
        var updated = data.GetProperty("updatedAt").GetDateTimeOffset().ToUniversalTime();
        var year = data.GetProperty("season").GetInt32();
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // All imports serialize their small publication transaction, including shared dictionary rows.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(749138201)", token);
        var hash = Hash(payload);
        if (await db.Snapshots.AnyAsync(row => row.ResourceKey == key && row.PayloadHash == hash, token)) return;
        var empty = (data.TryGetProperty("entries", out var entries) && entries.GetArrayLength() == 0)
            || (data.TryGetProperty("races", out var races) && races.GetArrayLength() == 0);
        var snapshot = new ResourceSnapshot { Id = Guid.NewGuid(), ResourceKey = key, Season = year,
            FetchedAt = updated, RefreshAfter = updated.AddMinutes(empty ? 1 : 15), Payload = payload, PayloadHash = hash };
        db.Snapshots.Add(snapshot);
        var projector = new SourceProjector(db);
        foreach (var page in pages.DistinctBy(page => Hash(page.Url + "\n" + page.Body)))
        {
            var id = Hash(page.Url + "\n" + page.Body);
            var document = await db.Documents.FindAsync([id], token);
            if (document is null)
            {
                document = new SourceDocument { Id = id, Url = page.Url, Payload = page.Body, PayloadHash = Hash(page.Body) };
                db.Documents.Add(document);
                await projector.ImportAsync(document, token);
            }
            db.ImportPages.Add(new ImportPage { SnapshotId = snapshot.Id, DocumentId = id, RetrievedAt = page.RetrievedAt.ToUniversalTime() });
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
