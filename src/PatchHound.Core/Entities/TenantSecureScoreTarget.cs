namespace PatchHound.Core.Entities;

public class TenantSecureScoreTarget
{
    public Guid TenantId { get; private set; }

    /// <summary>Target score the tenant aims to stay below (0–100, lower = better).</summary>
    public decimal TargetScore { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantSecureScoreTarget() { }

    public static TenantSecureScoreTarget CreateDefault(Guid tenantId)
    {
        return new TenantSecureScoreTarget
        {
            TenantId = tenantId,
            TargetScore = 40m,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(decimal targetScore)
    {
        TargetScore = Math.Clamp(targetScore, 0m, 100m);
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
