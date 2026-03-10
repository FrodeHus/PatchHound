namespace PatchHound.Api.Models.Software;

public record TenantSoftwareListItemDto(
    Guid Id,
    Guid NormalizedSoftwareId,
    string CanonicalName,
    string? CanonicalVendor,
    string Confidence,
    string NormalizationMethod,
    string? PrimaryCpe23Uri,
    int ActiveInstallCount,
    int UniqueDeviceCount,
    int ActiveVulnerabilityCount,
    int VersionCount,
    DateTimeOffset? LastSeenAt
);

public record TenantSoftwareFilterQuery(
    string? Search = null,
    string? Confidence = null,
    bool? VulnerableOnly = null,
    bool? BoundOnly = null
);
