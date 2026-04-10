namespace PatchHound.Core.Entities;

public class InstalledSoftware
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string Version { get; private set; } = "";
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private InstalledSoftware() { }

    public static InstalledSoftware Observe(
        Guid tenantId,
        Guid deviceId,
        Guid softwareProductId,
        Guid sourceSystemId,
        string? version,
        DateTimeOffset at)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));
        }
        if (softwareProductId == Guid.Empty)
        {
            throw new ArgumentException("SoftwareProductId is required.", nameof(softwareProductId));
        }
        if (sourceSystemId == Guid.Empty)
        {
            throw new ArgumentException("SourceSystemId is required.", nameof(sourceSystemId));
        }

        var normalizedVersion = version?.Trim() ?? "";
        if (normalizedVersion.Length > 128)
        {
            throw new ArgumentException("Version must be 128 characters or fewer.", nameof(version));
        }

        return new InstalledSoftware
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            SoftwareProductId = softwareProductId,
            SourceSystemId = sourceSystemId,
            Version = normalizedVersion,
            FirstSeenAt = at,
            LastSeenAt = at,
        };
    }

    public void Touch(DateTimeOffset at)
    {
        LastSeenAt = at;
    }
}
