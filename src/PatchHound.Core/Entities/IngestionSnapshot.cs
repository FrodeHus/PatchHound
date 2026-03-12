namespace PatchHound.Core.Entities;

public static class IngestionSnapshotStatuses
{
    public const string Building = "Building";
    public const string Published = "Published";
    public const string Discarded = "Discarded";
}

public class IngestionSnapshot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public Guid IngestionRunId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string Status { get; private set; } = string.Empty;

    private IngestionSnapshot() { }

    public static IngestionSnapshot Create(
        Guid tenantId,
        string sourceKey,
        Guid ingestionRunId,
        DateTimeOffset createdAt
    )
    {
        return new IngestionSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceKey = sourceKey,
            IngestionRunId = ingestionRunId,
            CreatedAt = createdAt,
            Status = IngestionSnapshotStatuses.Building,
        };
    }

    public void MarkPublished()
    {
        Status = IngestionSnapshotStatuses.Published;
    }

    public void Discard()
    {
        Status = IngestionSnapshotStatuses.Discarded;
    }
}
