namespace PatchHound.Core.Entities;

public class AssetRiskScore
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AssetId { get; private set; }
    public decimal OverallScore { get; private set; }
    public decimal MaxEpisodeRiskScore { get; private set; }
    public int CriticalCount { get; private set; }
    public int HighCount { get; private set; }
    public int MediumCount { get; private set; }
    public int LowCount { get; private set; }
    public int OpenEpisodeCount { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    public Asset Asset { get; private set; } = null!;

    private AssetRiskScore() { }

    public static AssetRiskScore Create(
        Guid tenantId,
        Guid assetId,
        decimal overallScore,
        decimal maxEpisodeRiskScore,
        int criticalCount,
        int highCount,
        int mediumCount,
        int lowCount,
        int openEpisodeCount,
        string factorsJson,
        string calculationVersion
    )
    {
        return new AssetRiskScore
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssetId = assetId,
            OverallScore = overallScore,
            MaxEpisodeRiskScore = maxEpisodeRiskScore,
            CriticalCount = criticalCount,
            HighCount = highCount,
            MediumCount = mediumCount,
            LowCount = lowCount,
            OpenEpisodeCount = openEpisodeCount,
            FactorsJson = factorsJson,
            CalculationVersion = calculationVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        decimal overallScore,
        decimal maxEpisodeRiskScore,
        int criticalCount,
        int highCount,
        int mediumCount,
        int lowCount,
        int openEpisodeCount,
        string factorsJson,
        string calculationVersion
    )
    {
        OverallScore = overallScore;
        MaxEpisodeRiskScore = maxEpisodeRiskScore;
        CriticalCount = criticalCount;
        HighCount = highCount;
        MediumCount = mediumCount;
        LowCount = lowCount;
        OpenEpisodeCount = openEpisodeCount;
        FactorsJson = factorsJson;
        CalculationVersion = calculationVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }
}
