namespace PatchHound.Core.Entities;

public class TenantSecureScoreSnapshot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal OverallScore { get; private set; }
    public int AssetCount { get; private set; }

    private TenantSecureScoreSnapshot() { }

    public static TenantSecureScoreSnapshot Create(
        Guid tenantId,
        DateOnly date,
        decimal overallScore,
        int assetCount)
    {
        return new TenantSecureScoreSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Date = date,
            OverallScore = overallScore,
            AssetCount = assetCount,
        };
    }

    public void Update(decimal overallScore, int assetCount)
    {
        OverallScore = overallScore;
        AssetCount = assetCount;
    }
}
