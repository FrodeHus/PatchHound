namespace PatchHound.Api.Models.Software;

public record TenantSoftwareListItemDto(
    Guid Id,
    Guid NormalizedSoftwareId,
    string CanonicalName,
    string? CanonicalVendor,
    string? Category,
    decimal? CurrentRiskScore,
    string? PrimaryCpe23Uri,
    int ActiveInstallCount,
    int UniqueDeviceCount,
    int ActiveVulnerabilityCount,
    int VersionCount,
    decimal? ExposureImpactScore,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? MaintenanceWindowDate
);

public record TenantSoftwareFilterQuery(
    string? Search = null,
    string? Category = null,
    bool? VulnerableOnly = null,
    bool? BoundOnly = null,
    bool? MissedMaintenanceWindow = null
);
