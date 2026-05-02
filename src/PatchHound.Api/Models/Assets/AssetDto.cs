using PatchHound.Core.Enums;

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
    IReadOnlyList<BusinessLabelSummaryDto> BusinessLabels,
    string? OnboardingStatus,
    string? DeviceValue
);


public record AssetCriticalityDetailDto(
    string Source,
    string? Reason,
    Guid? RuleId,
    DateTimeOffset? UpdatedAt
);

public record BusinessLabelSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string? Color,
    BusinessLabelWeightCategory WeightCategory,
    decimal RiskWeight
);

public record UpdateAssetBusinessLabelsRequest(
    IReadOnlyList<Guid> BusinessLabelIds
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
    Guid VulnerabilityId,
    string ExternalId,
    string Title,
    string RiskBand,
    decimal EpisodeRiskScore,
    decimal ThreatScore,
    decimal ContextScore,
    decimal OperationalScore
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
    Guid? BusinessLabelId = null,
    string? OnboardingStatus = null
);
