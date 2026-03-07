namespace PatchHound.Core.Entities;

public class DeviceSoftwareInstallationEpisode
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceAssetId { get; private set; }
    public Guid SoftwareAssetId { get; private set; }
    public int EpisodeNumber { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public int MissingSyncCount { get; private set; }

    public Asset DeviceAsset { get; private set; } = null!;
    public Asset SoftwareAsset { get; private set; } = null!;

    private DeviceSoftwareInstallationEpisode() { }

    public static DeviceSoftwareInstallationEpisode Create(
        Guid tenantId,
        Guid deviceAssetId,
        Guid softwareAssetId,
        int episodeNumber,
        DateTimeOffset firstSeenAt
    )
    {
        return new DeviceSoftwareInstallationEpisode
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceAssetId = deviceAssetId,
            SoftwareAssetId = softwareAssetId,
            EpisodeNumber = episodeNumber,
            FirstSeenAt = firstSeenAt,
            LastSeenAt = firstSeenAt,
            MissingSyncCount = 0,
        };
    }

    public void Seen(DateTimeOffset seenAt)
    {
        if (seenAt > LastSeenAt)
        {
            LastSeenAt = seenAt;
        }

        MissingSyncCount = 0;
        RemovedAt = null;
    }

    public void MarkMissing()
    {
        MissingSyncCount++;
    }

    public void Remove(DateTimeOffset removedAt)
    {
        RemovedAt = removedAt;
    }
}
