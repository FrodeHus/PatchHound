namespace PatchHound.Api.Models.Software;

public record NormalizedSoftwareListItemDto(
    Guid Id,
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

public record NormalizedSoftwareFilterQuery(
    string? Search = null,
    string? Confidence = null,
    bool? VulnerableOnly = null,
    bool? BoundOnly = null
);
