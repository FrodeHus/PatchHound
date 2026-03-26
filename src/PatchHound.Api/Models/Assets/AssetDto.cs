using PatchHound.Api.Models.Remediation;

namespace PatchHound.Api.Models.Assets;

public record AssetDto(
    Guid Id,
    string ExternalId,
    string Name,
    string AssetType,
    decimal? CurrentRiskScore,
    string? DeviceGroupName,
    string Criticality,
    string OwnerType,
    Guid? OwnerUserId,
    Guid? OwnerTeamId,
    string? SecurityProfileName,
    int VulnerabilityCount,
    int RecurringVulnerabilityCount,
    string? HealthStatus,
    string? RiskScore,
    string? ExposureLevel,
    string[] Tags,
    string? OnboardingStatus,
    string? DeviceValue
);

public record AssetDetailDto(
    Guid Id,
    Guid? TenantSoftwareId,
    string ExternalId,
    string Name,
    string? Description,
    string AssetType,
    string Criticality,
    AssetCriticalityDetailDto? CriticalityDetail,
    string OwnerType,
    string? OwnerUserName,
    Guid? OwnerUserId,
    string? OwnerTeamName,
    Guid? OwnerTeamId,
    string? FallbackTeamName,
    Guid? FallbackTeamId,
    AssetSecurityProfileSummaryDto? SecurityProfile,
    string? DeviceComputerDnsName,
    string? DeviceHealthStatus,
    string? DeviceOsPlatform,
    string? DeviceOsVersion,
    string? DeviceRiskScore,
    DateTimeOffset? DeviceLastSeenAt,
    string? DeviceLastIpAddress,
    string? DeviceAadDeviceId,
    string? DeviceGroupId,
    string? DeviceGroupName,
    string? DeviceExposureLevel,
    bool? DeviceIsAadJoined,
    string? DeviceOnboardingStatus,
    string? DeviceValue,
    AssetRiskDetailDto? Risk,
    RemediationTaskSummaryDto? Remediation,
    string[] Tags,
    SoftwareCpeBindingDto? SoftwareCpeBinding,
    string Metadata,
    IReadOnlyList<AssetVulnerabilityDto> Vulnerabilities,
    IReadOnlyList<AssetSoftwareInstallationDto> SoftwareInventory,
    IReadOnlyList<AssetKnownSoftwareVulnerabilityDto> KnownSoftwareVulnerabilities
);

public record AssetCriticalityDetailDto(
    string Source,
    string? Reason,
    Guid? RuleId,
    DateTimeOffset? UpdatedAt
);

public record AssetRiskDetailDto(
    decimal OverallScore,
    decimal MaxEpisodeRiskScore,
    string RiskBand,
    int OpenEpisodeCount,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    DateTimeOffset CalculatedAt,
    AssetRiskExplanationDto? Explanation,
    IReadOnlyList<AssetRiskDriverDto> TopDrivers
);

public record AssetRiskExplanationDto(
    decimal Score,
    string CalculationVersion,
    decimal MaxEpisodeRiskScore,
    decimal TopThreeAverage,
    decimal MaxEpisodeContribution,
    decimal TopThreeContribution,
    int OpenEpisodeCount,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    decimal CriticalContribution,
    decimal HighContribution,
    decimal MediumContribution,
    decimal LowContribution,
    IReadOnlyList<AssetRiskExplanationFactorDto> Factors
);

public record AssetRiskExplanationFactorDto(
    string Name,
    string Description,
    decimal Impact
);

public record AssetRiskDriverDto(
    Guid TenantVulnerabilityId,
    string ExternalId,
    string Title,
    string RiskBand,
    decimal EpisodeRiskScore,
    decimal ThreatScore,
    decimal ContextScore,
    decimal OperationalScore
);

public record AssetVulnerabilityDto(
    Guid VulnerabilityId,
    string ExternalId,
    string Title,
    string Description,
    string VendorSeverity,
    decimal? VendorScore,
    string? CvssVector,
    DateTimeOffset? PublishedDate,
    string EffectiveSeverity,
    decimal? EffectiveScore,
    string? AssessmentReasonSummary,
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
    Guid? TenantSoftwareId,
    string Name,
    string ExternalId,
    DateTimeOffset LastSeenAt,
    SoftwareCpeBindingDto? CpeBinding,
    int EpisodeCount,
    IReadOnlyList<AssetSoftwareInstallationEpisodeDto> Episodes
);

public record AssetSoftwareInstallationEpisodeDto(
    int EpisodeNumber,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RemovedAt
);

public record AssetKnownSoftwareVulnerabilityDto(
    Guid VulnerabilityId,
    string ExternalId,
    string Title,
    string VendorSeverity,
    decimal? CvssScore,
    string? CvssVector,
    string MatchMethod,
    string Confidence,
    string Evidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ResolvedAt
);

public record SoftwareCpeBindingDto(
    Guid Id,
    string Cpe23Uri,
    string BindingMethod,
    string Confidence,
    string? MatchedVendor,
    string? MatchedProduct,
    string? MatchedVersion,
    DateTimeOffset LastValidatedAt
);

public record AssetSecurityProfileSummaryDto(
    Guid Id,
    string Name,
    string EnvironmentClass,
    string InternetReachability,
    string ConfidentialityRequirement,
    string IntegrityRequirement,
    string AvailabilityRequirement
);

public record AssetFilterQuery(
    string? AssetType = null,
    string? Criticality = null,
    string? OwnerType = null,
    string? DeviceGroup = null,
    bool? UnassignedOnly = null,
    Guid? OwnerId = null,
    Guid? TenantId = null,
    string? Search = null,
    string? HealthStatus = null,
    string? RiskScore = null,
    string? ExposureLevel = null,
    string? Tag = null,
    string? OnboardingStatus = null
);
