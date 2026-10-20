using F1RaceLens.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using F1RaceLens.Api.Accounts;

namespace F1RaceLens.Api.Storage;

public sealed class RaceLensDbContext(DbContextOptions<RaceLensDbContext> options) : IdentityUserContext<ApplicationUser>(options)
{
    public DbSet<Predictions.Journal.ForecastRun> ForecastRuns => Set<Predictions.Journal.ForecastRun>();
    public DbSet<Predictions.Journal.ForecastSeal> ForecastSeals => Set<Predictions.Journal.ForecastSeal>();
    public DbSet<Predictions.Journal.ForecastScore> ForecastScores => Set<Predictions.Journal.ForecastScore>();
    public DbSet<ResourceSnapshot> Snapshots => Set<ResourceSnapshot>();
    public DbSet<SourceDocument> Documents => Set<SourceDocument>();
    public DbSet<ImportPage> ImportPages => Set<ImportPage>();
    public DbSet<SeasonRecord> Seasons => Set<SeasonRecord>();
    public DbSet<DriverRecord> Drivers => Set<DriverRecord>();
    public DbSet<ConstructorRecord> Constructors => Set<ConstructorRecord>();
    public DbSet<CircuitRecord> Circuits => Set<CircuitRecord>();
    public DbSet<RaceRecord> Races => Set<RaceRecord>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<ResultRecord> Results => Set<ResultRecord>();
    public DbSet<StandingRecord> Standings => Set<StandingRecord>();
    public DbSet<ParticipationRecord> Participations => Set<ParticipationRecord>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<ApplicationUser>().Property(x => x.Language).HasMaxLength(5);
        model.Entity<ApplicationUser>().Property(x => x.Theme).HasMaxLength(10);
        model.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        model.Entity<UserFavourite>().HasKey(x => new { x.UserId, x.Kind, x.EntityId });
        model.Entity<UserFavourite>().Property(x => x.Kind).HasMaxLength(10);
        model.Entity<UserFavourite>().Property(x => x.EntityId).HasMaxLength(80);
        model.Entity<UserFavourite>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Predictions.Journal.ForecastRun>(entity => {
            entity.HasIndex(x => new { x.Engine, x.OutputHash, x.InputHash }).IsUnique();
            entity.Property(x => x.Inputs).HasColumnType("text");
            entity.Property(x => x.Output).HasColumnType("text");
        });
        model.Entity<Predictions.Journal.ForecastSeal>(entity => {
            entity.HasIndex(x => new { x.Engine, x.Version, x.Season, x.Round, x.ThroughRound }).IsUnique();
            entity.HasOne<Predictions.Journal.ForecastRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Predictions.Journal.ForecastScore>(entity => {
            entity.HasIndex(x => new { x.SealId, x.ActualHash }).IsUnique();
            entity.Property(x => x.Actual).HasColumnType("text");
            entity.HasOne<Predictions.Journal.ForecastSeal>().WithMany().HasForeignKey(x => x.SealId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ResourceSnapshot>().HasIndex(x => new { x.ResourceKey, x.PayloadHash }).IsUnique();
        model.Entity<ResourceSnapshot>().HasIndex(x => new { x.ResourceKey, x.FetchedAt });
        model.Entity<ResourceSnapshot>().Property(x => x.Payload).HasColumnType("jsonb");
        model.Entity<ResourceSnapshot>().Property(x => x.ResourceKey).HasMaxLength(240);
        model.Entity<SourceDocument>().Property(x => x.Payload).HasColumnType("text");
        model.Entity<ImportPage>().HasKey(x => new { x.SnapshotId, x.DocumentId });
        model.Entity<ImportPage>().HasOne<ResourceSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ImportPage>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SeasonRecord>().HasKey(x => x.Year);
        model.Entity<RaceRecord>().HasKey(x => new { x.Season, x.Round });
        model.Entity<RaceRecord>().HasOne<SeasonRecord>().WithMany().HasForeignKey(x => x.Season).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RaceRecord>().HasOne<CircuitRecord>().WithMany().HasForeignKey(x => x.CircuitId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SessionRecord>().HasKey(x => new { x.Season, x.Round, x.Kind });
        model.Entity<SessionRecord>().HasOne<RaceRecord>().WithMany().HasForeignKey(x => new { x.Season, x.Round }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ResultRecord>().HasKey(x => new { x.DocumentId, x.Ordinal });
        model.Entity<ResultRecord>().HasIndex(x => new { x.Season, x.Round, x.Kind });
        model.Entity<ResultRecord>().Property(x => x.Points).HasColumnType("numeric");
        model.Entity<ResultRecord>().Property(x => x.Payload).HasColumnType("jsonb");
        model.Entity<ResultRecord>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ResultRecord>().HasOne<RaceRecord>().WithMany().HasForeignKey(x => new { x.Season, x.Round }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ResultRecord>().HasOne<DriverRecord>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ResultRecord>().HasOne<ConstructorRecord>().WithMany().HasForeignKey(x => x.ConstructorId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<StandingRecord>().HasKey(x => new { x.DocumentId, x.Ordinal });
        model.Entity<StandingRecord>().HasIndex(x => new { x.Season, x.Round, x.Kind });
        model.Entity<StandingRecord>().Property(x => x.Points).HasColumnType("numeric");
        model.Entity<StandingRecord>().Property(x => x.Payload).HasColumnType("jsonb");
        model.Entity<StandingRecord>().HasOne<SeasonRecord>().WithMany().HasForeignKey(x => x.Season).OnDelete(DeleteBehavior.Restrict);
        model.Entity<StandingRecord>().HasOne<SourceDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ParticipationRecord>().HasKey(x => new { x.Season, x.DriverId, x.ConstructorId });
        model.Entity<ParticipationRecord>().HasOne<SeasonRecord>().WithMany().HasForeignKey(x => x.Season).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ParticipationRecord>().HasOne<DriverRecord>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ParticipationRecord>().HasOne<ConstructorRecord>().WithMany().HasForeignKey(x => x.ConstructorId).OnDelete(DeleteBehavior.Restrict);
    }
}
