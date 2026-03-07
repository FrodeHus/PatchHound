namespace PatchHound.Api.Models.Assets;

public record AssetDto(
    Guid Id,
    string ExternalId,
    string Name,
    string AssetType,
    string Criticality,
    string OwnerType,
    int VulnerabilityCount,
    int RecurringVulnerabilityCount
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
    string? DeviceComputerDnsName,
    string? DeviceHealthStatus,
    string? DeviceOsPlatform,
    string? DeviceOsVersion,
    string? DeviceRiskScore,
    DateTimeOffset? DeviceLastSeenAt,
    string? DeviceLastIpAddress,
    string? DeviceAadDeviceId,
    string Metadata,
    IReadOnlyList<AssetVulnerabilityDto> Vulnerabilities,
    IReadOnlyList<AssetSoftwareInstallationDto> SoftwareInventory
);

public record AssetVulnerabilityDto(
    Guid VulnerabilityId,
    string ExternalId,
    string Title,
    string VendorSeverity,
    string Status,
    DateTimeOffset DetectedDate,
    DateTimeOffset? ResolvedDate,
    int EpisodeCount,
    IReadOnlyList<AssetVulnerabilityEpisodeDto> Episodes,
    IReadOnlyList<string> PossibleCorrelatedSoftware
);

public record AssetVulnerabilityEpisodeDto(
    int EpisodeNumber,
    string Status,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ResolvedAt
);

public record AssetSoftwareInstallationDto(
    Guid SoftwareAssetId,
    string Name,
    string ExternalId,
    DateTimeOffset LastSeenAt,
    int EpisodeCount,
    IReadOnlyList<AssetSoftwareInstallationEpisodeDto> Episodes
);

public record AssetSoftwareInstallationEpisodeDto(
    int EpisodeNumber,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RemovedAt
);

public record AssetFilterQuery(
    string? AssetType = null,
    string? OwnerType = null,
    Guid? OwnerId = null,
    Guid? TenantId = null,
    string? Search = null
);
