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
    public Guid? LastSeenRunId { get; private set; }

    private InstalledSoftware() { }

    public static InstalledSoftware Observe(
        Guid tenantId,
        Guid deviceId,
        Guid softwareProductId,
        Guid sourceSystemId,
        string? version,
        DateTimeOffset at,
        Guid? runId = null)
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
        if (at == default)
        {
            throw new ArgumentException("Observation timestamp is required.", nameof(at));
        }
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId cannot be empty.", nameof(runId));
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
            LastSeenRunId = runId,
        };
    }

    public void Touch(DateTimeOffset at, Guid? runId = null)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId cannot be empty.", nameof(runId));
        }

        if (at > LastSeenAt)
        {
            LastSeenAt = at;
        }

        if (runId.HasValue)
        {
            LastSeenRunId = runId;
        }
    }
}
