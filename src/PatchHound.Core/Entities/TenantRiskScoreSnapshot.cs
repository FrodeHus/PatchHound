namespace PatchHound.Core.Entities;

public class TenantRiskScoreSnapshot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal OverallScore { get; private set; }
    public int AssetCount { get; private set; }
    public int CriticalAssetCount { get; private set; }
    public int HighAssetCount { get; private set; }

    private TenantRiskScoreSnapshot() { }

    public static TenantRiskScoreSnapshot Create(
        Guid tenantId,
        DateOnly date,
        decimal overallScore,
        int assetCount,
        int criticalAssetCount,
        int highAssetCount
    )
    {
        return new TenantRiskScoreSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Date = date,
            OverallScore = overallScore,
            AssetCount = assetCount,
            CriticalAssetCount = criticalAssetCount,
            HighAssetCount = highAssetCount,
        };
    }

    public void Update(decimal overallScore, int assetCount, int criticalAssetCount, int highAssetCount)
    {
        OverallScore = overallScore;
        AssetCount = assetCount;
        CriticalAssetCount = criticalAssetCount;
        HighAssetCount = highAssetCount;
    }
}
