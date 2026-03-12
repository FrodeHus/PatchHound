namespace PatchHound.Core.Entities;

public class TenantSoftware
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? SnapshotId { get; private set; }
    public Guid NormalizedSoftwareId { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public NormalizedSoftware NormalizedSoftware { get; private set; } = null!;

    private TenantSoftware() { }

    public static TenantSoftware Create(
        Guid tenantId,
        Guid? snapshotId,
        Guid normalizedSoftwareId,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt
    )
    {
        return new TenantSoftware
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SnapshotId = snapshotId,
            NormalizedSoftwareId = normalizedSoftwareId,
            FirstSeenAt = firstSeenAt,
            LastSeenAt = lastSeenAt,
            CreatedAt = firstSeenAt,
            UpdatedAt = lastSeenAt,
        };
    }

    public void UpdateObservationWindow(DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt)
    {
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        UpdatedAt = lastSeenAt;
    }

    public void AssignSnapshot(Guid? snapshotId)
    {
        SnapshotId = snapshotId;
    }
}
