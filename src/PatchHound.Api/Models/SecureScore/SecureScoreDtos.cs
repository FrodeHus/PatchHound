namespace PatchHound.Api.Models.SecureScore;

public record SecureScoreSummaryDto(
    decimal OverallScore,
    decimal TargetScore,
    int AssetCount,
    int AssetsAboveTarget,
    List<AssetScoreSummaryDto> TopRiskAssets
);

public record AssetScoreSummaryDto(
    Guid AssetId,
    string AssetName,
    decimal OverallScore,
    decimal VulnerabilityScore,
    decimal ConfigurationScore,
    decimal DeviceValueWeight,
    int ActiveVulnerabilityCount
);

public record AssetScoreDetailDto(
    Guid AssetId,
    string AssetName,
    decimal OverallScore,
    decimal VulnerabilityScore,
    decimal ConfigurationScore,
    decimal DeviceValueWeight,
    int ActiveVulnerabilityCount,
    List<ScoreFactorDto> Factors,
    DateTimeOffset CalculatedAt,
    string CalculationVersion
);

public record ScoreFactorDto(string Name, string Description, decimal Impact);

public record UpdateTargetScoreRequest(decimal TargetScore);
