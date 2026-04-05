namespace PatchHound.Core.Entities.AuthenticatedScans;

public class AssetScanProfileAssignment
{
    public Guid TenantId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid ScanProfileId { get; private set; }
    public Guid? AssignedByRuleId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    private AssetScanProfileAssignment() { }

    public static AssetScanProfileAssignment Create(Guid tenantId, Guid assetId, Guid scanProfileId, Guid? assignedByRuleId) =>
        new()
        {
            TenantId = tenantId,
            AssetId = assetId,
            ScanProfileId = scanProfileId,
            AssignedByRuleId = assignedByRuleId,
            AssignedAt = DateTimeOffset.UtcNow,
        };
}
