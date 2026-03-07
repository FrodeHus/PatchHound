namespace PatchHound.Api.Models.Assets;

public record AssetDto(
    Guid Id,
    string ExternalId,
    string Name,
    string AssetType,
    string Criticality,
    string OwnerType,
    int VulnerabilityCount
);

public record AssetDetailDto(
    Guid Id,
    string ExternalId,
    string Name,
    string? Description,
    string AssetType,
    string Criticality,
    string OwnerType,
    Guid? OwnerUserId,
    Guid? OwnerTeamId,
    Guid? FallbackTeamId,
    string Metadata,
    IReadOnlyList<AssetVulnerabilityDto> Vulnerabilities
);

public record AssetVulnerabilityDto(
    Guid VulnerabilityId,
    string ExternalId,
    string Title,
    string VendorSeverity,
    string Status,
    DateTimeOffset DetectedDate,
    DateTimeOffset? ResolvedDate
);

public record AssetFilterQuery(
    string? AssetType = null,
    string? OwnerType = null,
    Guid? OwnerId = null,
    Guid? TenantId = null,
    string? Search = null
);
