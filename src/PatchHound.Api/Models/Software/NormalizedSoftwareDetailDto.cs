namespace PatchHound.Api.Models.Software;

public record NormalizedSoftwareDetailDto(
    Guid Id,
    string CanonicalName,
    string? CanonicalVendor,
    string? PrimaryCpe23Uri,
    string NormalizationMethod,
    string Confidence,
    DateTimeOffset? FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    int ActiveInstallCount,
    int UniqueDeviceCount,
    int VulnerableInstallCount,
    int ActiveVulnerabilityCount,
    int VersionCount,
    IReadOnlyList<NormalizedSoftwareVersionCohortDto> VersionCohorts,
    IReadOnlyList<NormalizedSoftwareSourceAliasDto> SourceAliases
);

public record NormalizedSoftwareVersionCohortDto(
    string? Version,
    int ActiveInstallCount,
    int DeviceCount,
    int ActiveVulnerabilityCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt
);

public record NormalizedSoftwareSourceAliasDto(
    string SourceSystem,
    string ExternalSoftwareId,
    string RawName,
    string? RawVendor,
    string? RawVersion,
    string AliasConfidence,
    string MatchReason
);

public record NormalizedSoftwareInstallationDto(
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
    Guid? OwnerTeamId,
    int OpenVulnerabilityCount
);

public record NormalizedSoftwareVulnerabilityDto(
    Guid VulnerabilityId,
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
    IReadOnlyList<NormalizedSoftwareVulnerabilityEvidenceDto> Evidence
);

public record NormalizedSoftwareVulnerabilityEvidenceDto(
    string Method,
    string Confidence,
    string Evidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ResolvedAt
);
