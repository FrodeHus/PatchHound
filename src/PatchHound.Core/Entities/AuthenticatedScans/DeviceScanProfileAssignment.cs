namespace PatchHound.Core.Entities.AuthenticatedScans;

public class DeviceScanProfileAssignment
{
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid ScanProfileId { get; private set; }
    public Guid? AssignedByRuleId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    private DeviceScanProfileAssignment() { }

    public static DeviceScanProfileAssignment Create(Guid tenantId, Guid deviceId, Guid scanProfileId, Guid? assignedByRuleId) =>
        new()
        {
            TenantId = tenantId,
            DeviceId = deviceId,
            ScanProfileId = scanProfileId,
            AssignedByRuleId = assignedByRuleId,
            AssignedAt = DateTimeOffset.UtcNow,
        };
}
