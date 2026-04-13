namespace PatchHound.Core.Entities.AuthenticatedScans;

public class StagedDetectedSoftware
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ScanJobId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid ScanProfileId { get; private set; }
    public string CanonicalName { get; private set; } = string.Empty;
    public string CanonicalProductKey { get; private set; } = string.Empty;
    public string? CanonicalVendor { get; private set; }
    public string? Category { get; private set; }
    public string? PrimaryCpe23Uri { get; private set; }
    public string? DetectedVersion { get; private set; }
    public DateTimeOffset StagedAt { get; private set; }

    private StagedDetectedSoftware() { }

    public static StagedDetectedSoftware Create(
        Guid tenantId, Guid scanJobId, Guid deviceId, Guid scanProfileId,
        string canonicalName, string canonicalProductKey,
        string? canonicalVendor, string? category, string? primaryCpe23Uri, string? detectedVersion) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScanJobId = scanJobId,
            DeviceId = deviceId,
            ScanProfileId = scanProfileId,
            CanonicalName = canonicalName.Trim(),
            CanonicalProductKey = canonicalProductKey.Trim(),
            CanonicalVendor = string.IsNullOrWhiteSpace(canonicalVendor) ? null : canonicalVendor.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            PrimaryCpe23Uri = string.IsNullOrWhiteSpace(primaryCpe23Uri) ? null : primaryCpe23Uri.Trim(),
            DetectedVersion = string.IsNullOrWhiteSpace(detectedVersion) ? null : detectedVersion.Trim(),
            StagedAt = DateTimeOffset.UtcNow,
        };
}
