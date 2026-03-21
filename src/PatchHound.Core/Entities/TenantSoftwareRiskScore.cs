namespace PatchHound.Core.Entities;

public class TenantSoftwareRiskScore
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TenantSoftwareId { get; private set; }
    public Guid? SnapshotId { get; private set; }
    public decimal OverallScore { get; private set; }
    public decimal MaxEpisodeRiskScore { get; private set; }
    public int CriticalEpisodeCount { get; private set; }
    public int HighEpisodeCount { get; private set; }
    public int MediumEpisodeCount { get; private set; }
    public int LowEpisodeCount { get; private set; }
    public int AffectedDeviceCount { get; private set; }
    public int OpenEpisodeCount { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    public TenantSoftware TenantSoftware { get; private set; } = null!;

    private TenantSoftwareRiskScore() { }

    public static TenantSoftwareRiskScore Create(
        Guid tenantId,
        Guid tenantSoftwareId,
        Guid? snapshotId,
        decimal overallScore,
        decimal maxEpisodeRiskScore,
        int criticalEpisodeCount,
        int highEpisodeCount,
        int mediumEpisodeCount,
        int lowEpisodeCount,
        int affectedDeviceCount,
        int openEpisodeCount,
        string factorsJson,
        string calculationVersion
    )
    {
        return new TenantSoftwareRiskScore
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TenantSoftwareId = tenantSoftwareId,
            SnapshotId = snapshotId,
            OverallScore = overallScore,
            MaxEpisodeRiskScore = maxEpisodeRiskScore,
            CriticalEpisodeCount = criticalEpisodeCount,
            HighEpisodeCount = highEpisodeCount,
            MediumEpisodeCount = mediumEpisodeCount,
            LowEpisodeCount = lowEpisodeCount,
            AffectedDeviceCount = affectedDeviceCount,
            OpenEpisodeCount = openEpisodeCount,
            FactorsJson = factorsJson,
            CalculationVersion = calculationVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        Guid? snapshotId,
        decimal overallScore,
        decimal maxEpisodeRiskScore,
        int criticalEpisodeCount,
        int highEpisodeCount,
        int mediumEpisodeCount,
        int lowEpisodeCount,
        int affectedDeviceCount,
        int openEpisodeCount,
        string factorsJson,
        string calculationVersion
    )
    {
        SnapshotId = snapshotId;
        OverallScore = overallScore;
        MaxEpisodeRiskScore = maxEpisodeRiskScore;
        CriticalEpisodeCount = criticalEpisodeCount;
        HighEpisodeCount = highEpisodeCount;
        MediumEpisodeCount = mediumEpisodeCount;
        LowEpisodeCount = lowEpisodeCount;
        AffectedDeviceCount = affectedDeviceCount;
        OpenEpisodeCount = openEpisodeCount;
        FactorsJson = factorsJson;
        CalculationVersion = calculationVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }
}
