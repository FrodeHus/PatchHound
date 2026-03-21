namespace PatchHound.Core.Entities;

public class TeamRiskScore
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TeamId { get; private set; }
    public decimal OverallScore { get; private set; }
    public decimal MaxAssetRiskScore { get; private set; }
    public int CriticalEpisodeCount { get; private set; }
    public int HighEpisodeCount { get; private set; }
    public int MediumEpisodeCount { get; private set; }
    public int LowEpisodeCount { get; private set; }
    public int AssetCount { get; private set; }
    public int OpenEpisodeCount { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    public Team Team { get; private set; } = null!;

    private TeamRiskScore() { }

    public static TeamRiskScore Create(
        Guid tenantId,
        Guid teamId,
        decimal overallScore,
        decimal maxAssetRiskScore,
        int criticalEpisodeCount,
        int highEpisodeCount,
        int mediumEpisodeCount,
        int lowEpisodeCount,
        int assetCount,
        int openEpisodeCount,
        string factorsJson,
        string calculationVersion
    )
    {
        return new TeamRiskScore
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TeamId = teamId,
            OverallScore = overallScore,
            MaxAssetRiskScore = maxAssetRiskScore,
            CriticalEpisodeCount = criticalEpisodeCount,
            HighEpisodeCount = highEpisodeCount,
            MediumEpisodeCount = mediumEpisodeCount,
            LowEpisodeCount = lowEpisodeCount,
            AssetCount = assetCount,
            OpenEpisodeCount = openEpisodeCount,
            FactorsJson = factorsJson,
            CalculationVersion = calculationVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        decimal overallScore,
        decimal maxAssetRiskScore,
        int criticalEpisodeCount,
        int highEpisodeCount,
        int mediumEpisodeCount,
        int lowEpisodeCount,
        int assetCount,
        int openEpisodeCount,
        string factorsJson,
        string calculationVersion
    )
    {
        OverallScore = overallScore;
        MaxAssetRiskScore = maxAssetRiskScore;
        CriticalEpisodeCount = criticalEpisodeCount;
        HighEpisodeCount = highEpisodeCount;
        MediumEpisodeCount = mediumEpisodeCount;
        LowEpisodeCount = lowEpisodeCount;
        AssetCount = assetCount;
        OpenEpisodeCount = openEpisodeCount;
        FactorsJson = factorsJson;
        CalculationVersion = calculationVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }
}
