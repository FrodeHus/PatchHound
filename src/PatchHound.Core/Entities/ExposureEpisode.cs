namespace PatchHound.Core.Entities;

public class ExposureEpisode
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceVulnerabilityExposureId { get; private set; }
    public int EpisodeNumber { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public DeviceVulnerabilityExposure Exposure { get; private set; } = null!;

    private ExposureEpisode() { }

    public static ExposureEpisode Open(
        Guid tenantId,
        Guid deviceVulnerabilityExposureId,
        int episodeNumber,
        DateTimeOffset seenAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException(nameof(tenantId));
        if (deviceVulnerabilityExposureId == Guid.Empty) throw new ArgumentException(nameof(deviceVulnerabilityExposureId));
        if (episodeNumber <= 0) throw new ArgumentOutOfRangeException(nameof(episodeNumber));
        if (seenAt == default) throw new ArgumentException(nameof(seenAt));

        return new ExposureEpisode
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceVulnerabilityExposureId = deviceVulnerabilityExposureId,
            EpisodeNumber = episodeNumber,
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt,
        };
    }

    public void Close(DateTimeOffset closedAt)
    {
        if (closedAt < LastSeenAt)
        {
            closedAt = LastSeenAt;
        }

        ClosedAt = closedAt;
    }
}
