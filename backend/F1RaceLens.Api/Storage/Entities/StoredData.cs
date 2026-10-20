namespace F1RaceLens.Api.Storage.Entities;

public sealed class ResourceSnapshot
{
    public Guid Id { get; set; }
    public string ResourceKey { get; set; } = "";
    public int Season { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset RefreshAfter { get; set; }
    public string PayloadHash { get; set; } = "";
    public string Payload { get; set; } = "{}";
}
public sealed class SourceDocument
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public string PayloadHash { get; set; } = "";
    public string AdapterVersion { get; set; } = "jolpica-v1";
}
public sealed class ImportPage
{
    public Guid SnapshotId { get; set; }
    public string DocumentId { get; set; } = "";
    public DateTimeOffset RetrievedAt { get; set; }
}
public sealed class SeasonRecord { public int Year { get; set; } }
public sealed class DriverRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Nationality { get; set; }
}
public sealed class ConstructorRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
public sealed class CircuitRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Country { get; set; }
    public string? Locality { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
public sealed class RaceRecord
{
    public int Season { get; set; }
    public int Round { get; set; }
    public string Name { get; set; } = "";
    public string CircuitId { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
}
public sealed class SessionRecord
{
    public int Season { get; set; }
    public int Round { get; set; }
    public string Kind { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
}
// A row is identified by its source document and ordinal, never just driver + session.
public sealed class ResultRecord
{
    public string DocumentId { get; set; } = "";
    public int Ordinal { get; set; }
    public int Season { get; set; }
    public int Round { get; set; }
    public string Kind { get; set; } = "";
    public string DriverId { get; set; } = "";
    public string? ConstructorId { get; set; }
    public string? CarNumber { get; set; }
    public int? Position { get; set; }
    public string? PositionText { get; set; }
    public int? Grid { get; set; }
    public string? Status { get; set; }
    public decimal? Points { get; set; }
    public string Payload { get; set; } = "{}";
}
public sealed class StandingRecord
{
    public string DocumentId { get; set; } = "";
    public int Ordinal { get; set; }
    public int Season { get; set; }
    public int? Round { get; set; }
    public string Kind { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public string? PositionText { get; set; }
    public decimal Points { get; set; }
    public int Wins { get; set; }
    public string Payload { get; set; } = "{}";
}
public sealed class ParticipationRecord
{
    public int Season { get; set; }
    public string DriverId { get; set; } = "";
    public string ConstructorId { get; set; } = "";
}
