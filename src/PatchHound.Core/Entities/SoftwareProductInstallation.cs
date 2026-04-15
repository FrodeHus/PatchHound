using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class SoftwareProductInstallation
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? SnapshotId { get; private set; }
    public Guid TenantSoftwareId { get; private set; }
    public Guid SoftwareAssetId { get; private set; }
    public Guid DeviceAssetId { get; private set; }
    public SoftwareIdentitySourceSystem SourceSystem { get; private set; }
    public string? DetectedVersion { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public bool IsActive { get; private set; }
    public int CurrentEpisodeNumber { get; private set; }

    public SoftwareTenantRecord TenantSoftware { get; private set; } = null!;
    public Device DeviceAsset { get; private set; } = null!;

    private SoftwareProductInstallation() { }

    public static SoftwareProductInstallation Create(
        Guid tenantId,
        Guid? snapshotId,
        Guid tenantSoftwareId,
        Guid softwareAssetId,
        Guid deviceAssetId,
        SoftwareIdentitySourceSystem sourceSystem,
        string? detectedVersion,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset? removedAt,
        bool isActive,
        int currentEpisodeNumber
    )
    {
        return new SoftwareProductInstallation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SnapshotId = snapshotId,
            TenantSoftwareId = tenantSoftwareId,
            SoftwareAssetId = softwareAssetId,
            DeviceAssetId = deviceAssetId,
            SourceSystem = sourceSystem,
            DetectedVersion = string.IsNullOrWhiteSpace(detectedVersion) ? null : detectedVersion.Trim(),
            FirstSeenAt = firstSeenAt,
            LastSeenAt = lastSeenAt,
            RemovedAt = removedAt,
            IsActive = isActive,
            CurrentEpisodeNumber = currentEpisodeNumber,
        };
    }

    public void UpdateProjection(
        Guid? snapshotId,
        Guid tenantSoftwareId,
        SoftwareIdentitySourceSystem sourceSystem,
        string? detectedVersion,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset? removedAt,
        bool isActive,
        int currentEpisodeNumber
    )
    {
        SnapshotId = snapshotId;
        TenantSoftwareId = tenantSoftwareId;
        SourceSystem = sourceSystem;
        DetectedVersion = string.IsNullOrWhiteSpace(detectedVersion) ? null : detectedVersion.Trim();
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        RemovedAt = removedAt;
        IsActive = isActive;
        CurrentEpisodeNumber = currentEpisodeNumber;
    }
}
