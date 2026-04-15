namespace PatchHound.Core.Entities;

public class DeviceSoftwareInstallation
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceAssetId { get; private set; }
    public Guid SoftwareAssetId { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public int MissingSyncCount { get; private set; }

    public Device DeviceAsset { get; private set; } = null!;

    private DeviceSoftwareInstallation() { }

    public static DeviceSoftwareInstallation Create(
        Guid tenantId,
        Guid deviceAssetId,
        Guid softwareAssetId,
        DateTimeOffset lastSeenAt
    )
    {
        return new DeviceSoftwareInstallation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceAssetId = deviceAssetId,
            SoftwareAssetId = softwareAssetId,
            LastSeenAt = lastSeenAt,
        };
    }

    public void Touch(DateTimeOffset lastSeenAt)
    {
        LastSeenAt = lastSeenAt;
        MissingSyncCount = 0;
    }

    public void MarkMissing()
    {
        MissingSyncCount++;
    }
}
