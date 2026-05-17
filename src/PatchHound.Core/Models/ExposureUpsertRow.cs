namespace PatchHound.Core.Models;

public sealed record ExposureUpsertRow(
    Guid TenantId,
    Guid DeviceId,
    Guid VulnerabilityId,
    Guid? SoftwareProductId,
    Guid? InstalledSoftwareId,
    string MatchedVersion,
    string MatchSource,   // "Product" | "Cpe"
    DateTimeOffset ObservedAt,
    Guid RunId);

public sealed record BulkExposureUpsertResult(int Inserted, int Reobserved);
