namespace PatchHound.Core.Entities;

public class AssetSecureScore
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AssetId { get; private set; }

    /// <summary>Composite score 0–100 (100 = worst exposure).</summary>
    public decimal OverallScore { get; private set; }

    /// <summary>Vulnerability sub-score 0–100.</summary>
    public decimal VulnerabilityScore { get; private set; }

    /// <summary>Placeholder hardening sub-score 0–100 (lower = better).</summary>
    public decimal ConfigurationScore { get; private set; }

    /// <summary>Device-value multiplier applied (High=1.5, Normal=1.0, Low=0.8).</summary>
    public decimal DeviceValueWeight { get; private set; }

    /// <summary>Number of active vulnerability-asset assessments used.</summary>
    public int ActiveVulnerabilityCount { get; private set; }

    /// <summary>JSON-serialised factor breakdown for the detail view.</summary>
    public string FactorsJson { get; private set; } = "[]";

    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    public Asset Asset { get; private set; } = null!;

    private AssetSecureScore() { }

    public static AssetSecureScore Create(
        Guid tenantId,
        Guid assetId,
        decimal overallScore,
        decimal vulnerabilityScore,
        decimal configurationScore,
        decimal deviceValueWeight,
        int activeVulnerabilityCount,
        string factorsJson,
        string calculationVersion
    )
    {
        return new AssetSecureScore
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssetId = assetId,
            OverallScore = overallScore,
            VulnerabilityScore = vulnerabilityScore,
            ConfigurationScore = configurationScore,
            DeviceValueWeight = deviceValueWeight,
            ActiveVulnerabilityCount = activeVulnerabilityCount,
            FactorsJson = factorsJson,
            CalculationVersion = calculationVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        decimal overallScore,
        decimal vulnerabilityScore,
        decimal configurationScore,
        decimal deviceValueWeight,
        int activeVulnerabilityCount,
        string factorsJson,
        string calculationVersion
    )
    {
        OverallScore = overallScore;
        VulnerabilityScore = vulnerabilityScore;
        ConfigurationScore = configurationScore;
        DeviceValueWeight = deviceValueWeight;
        ActiveVulnerabilityCount = activeVulnerabilityCount;
        FactorsJson = factorsJson;
        CalculationVersion = calculationVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }
}
