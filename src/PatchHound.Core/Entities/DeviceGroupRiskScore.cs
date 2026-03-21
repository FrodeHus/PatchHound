namespace PatchHound.Core.Entities;

public class DeviceGroupRiskScore
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string GroupKey { get; private set; } = null!;
    public string? DeviceGroupId { get; private set; }
    public string DeviceGroupName { get; private set; } = null!;
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

    private DeviceGroupRiskScore() { }

    public static DeviceGroupRiskScore Create(
        Guid tenantId,
        string groupKey,
        string? deviceGroupId,
        string deviceGroupName,
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
        return new DeviceGroupRiskScore
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GroupKey = groupKey,
            DeviceGroupId = deviceGroupId,
            DeviceGroupName = deviceGroupName,
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
        string? deviceGroupId,
        string deviceGroupName,
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
        DeviceGroupId = deviceGroupId;
        DeviceGroupName = deviceGroupName;
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
