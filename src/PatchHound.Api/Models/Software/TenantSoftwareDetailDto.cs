using PatchHound.Api.Models.Remediation;

namespace PatchHound.Api.Models.Software;

public record TenantSoftwareDetailDto(
    Guid Id,
    Guid NormalizedSoftwareId,
    Guid? PrimarySoftwareAssetId,
    string CanonicalName,
    string? CanonicalVendor,
    string? PrimaryCpe23Uri,
    string? Description,
    DateTimeOffset? DescriptionGeneratedAt,
    string? DescriptionProviderType,
    string? DescriptionProfileName,
    string? DescriptionModel,
    string NormalizationMethod,
    string Confidence,
    DateTimeOffset? FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    int ActiveInstallCount,
    int UniqueDeviceCount,
    int VulnerableInstallCount,
    int ActiveVulnerabilityCount,
    int VersionCount,
    decimal? ExposureImpactScore,
    ExposureImpactExplanationDto? ExposureImpactExplanation,
    RemediationTaskSummaryDto Remediation,
    IReadOnlyList<TenantSoftwareVersionCohortDto> VersionCohorts,
    IReadOnlyList<TenantSoftwareSourceAliasDto> SourceAliases
);

public record ExposureImpactExplanationDto(
    decimal Score,
    string CalculationVersion,
    int DeviceCount,
    int HighValueDeviceCount,
    decimal DeviceReachWeight,
    decimal HighValueRatio,
    decimal HighValueBonus,
    int VulnerabilityCount,
    decimal RawVulnerabilitySum,
    decimal VulnerabilityComponent,
    decimal RawScore,
    IReadOnlyList<ExposureImpactFactorDto> VulnerabilityFactors
);

public record ExposureImpactFactorDto(
    string ExternalId,
    string Severity,
    decimal? CvssScore,
    decimal SeverityWeight,
    decimal NormalizedScore,
    decimal Contribution
);

public record TenantSoftwareVersionCohortDto(
    string? Version,
    int ActiveInstallCount,
    int DeviceCount,
    int ActiveVulnerabilityCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt
);

public record TenantSoftwareSourceAliasDto(
    string SourceSystem,
    string ExternalSoftwareId,
    string RawName,
    string? RawVendor,
    string? RawVersion,
    string AliasConfidence,
    string MatchReason
);

public record TenantSoftwareInstallationDto(
    Guid TenantSoftwareId,
    Guid DeviceAssetId,
    string DeviceName,
    string DeviceCriticality,
    Guid SoftwareAssetId,
    string SoftwareAssetName,
    string? Version,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RemovedAt,
    bool IsActive,
    int CurrentEpisodeNumber,
    string? SecurityProfileName,
    Guid? OwnerUserId,
    string? OwnerUserName,
    Guid? OwnerTeamId,
    string? OwnerTeamName,
    int OpenVulnerabilityCount
);

public record TenantSoftwareVulnerabilityDto(
    Guid TenantVulnerabilityId,
    Guid VulnerabilityDefinitionId,
    string ExternalId,
    string Title,
    string VendorSeverity,
    decimal? CvssScore,
    DateTimeOffset? PublishedDate,
    string Source,
    string BestMatchMethod,
    string BestConfidence,
    int AffectedInstallCount,
    int AffectedDeviceCount,
    int AffectedVersionCount,
    IReadOnlyList<string> AffectedVersions,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<TenantSoftwareVulnerabilityEvidenceDto> Evidence
);

public record TenantSoftwareVulnerabilityEvidenceDto(
    string Method,
    string Confidence,
    string Evidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ResolvedAt
);

public record TenantSoftwareDescriptionDto(
    Guid TenantSoftwareId,
    Guid NormalizedSoftwareId,
    string Description,
    string ProviderType,
    string ProfileName,
    string Model,
    DateTimeOffset GeneratedAt
);
