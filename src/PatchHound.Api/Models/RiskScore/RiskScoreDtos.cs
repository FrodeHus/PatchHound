namespace PatchHound.Api.Models.RiskScore;

public record RiskScoreSummaryDto(
    decimal OverallScore,
    int AssetCount,
    int CriticalAssetCount,
    int HighAssetCount,
    List<AssetRiskScoreSummaryDto> TopRiskAssets,
    List<RiskScoreSnapshotDto> History,
    DateTimeOffset? CalculatedAt
);

public record AssetRiskScoreSummaryDto(
    Guid AssetId,
    string AssetName,
    decimal OverallScore,
    decimal MaxEpisodeRiskScore,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    int OpenEpisodeCount,
    List<AssetRiskEpisodeDriverDto> EpisodeDrivers
);

public record AssetRiskEpisodeDriverDto(
    Guid VulnerabilityId,
    string ExternalId,
    string Title,
    string RiskBand,
    decimal EpisodeRiskScore,
    decimal ThreatScore,
    decimal ContextScore,
    decimal OperationalScore
);

public record RiskScoreSnapshotDto(
    DateOnly Date,
    decimal OverallScore,
    int AssetCount,
    int CriticalAssetCount,
    int HighAssetCount
);

public record DeviceGroupRiskDetailDto(
    string DeviceGroupName,
    decimal OverallScore,
    DateTimeOffset CalculatedAt,
    int AssetCount,
    int OpenEpisodeCount,
    int CriticalEpisodeCount,
    int HighEpisodeCount,
    int MediumEpisodeCount,
    int LowEpisodeCount,
    RollupRiskExplanationDto? Explanation,
    List<AssetRiskScoreSummaryDto> TopRiskAssets
);

public record SoftwareRiskDetailDto(
    Guid TenantSoftwareId,
    string SoftwareName,
    string? Vendor,
    decimal OverallScore,
    DateTimeOffset CalculatedAt,
    int AffectedDeviceCount,
    int OpenEpisodeCount,
    int CriticalEpisodeCount,
    int HighEpisodeCount,
    int MediumEpisodeCount,
    int LowEpisodeCount,
    List<AssetRiskScoreSummaryDto> TopRiskAssets
);

public record RollupRiskExplanationDto(
    decimal Score,
    string CalculationVersion,
    decimal MaxAssetRiskScore,
    decimal TopThreeAverage,
    decimal MaxAssetContribution,
    decimal TopThreeContribution,
    int AssetCount,
    int OpenEpisodeCount,
    int CriticalEpisodeCount,
    int HighEpisodeCount,
    int MediumEpisodeCount,
    int LowEpisodeCount,
    decimal CriticalContribution,
    decimal HighContribution,
    decimal MediumContribution,
    decimal LowContribution,
    IReadOnlyList<RollupRiskExplanationFactorDto> Factors
);

public record RollupRiskExplanationFactorDto(
    string Name,
    string Description,
    decimal Impact
);
