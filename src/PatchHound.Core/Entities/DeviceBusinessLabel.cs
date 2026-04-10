namespace PatchHound.Core.Entities;

public class DeviceBusinessLabel
{
    public const string ManualSourceType = "Manual";
    public const string RuleSourceType = "Rule";
    public const string ManualSourceKey = "manual";

    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid BusinessLabelId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string SourceKey { get; private set; } = null!;
    public DateTimeOffset AssignedAt { get; private set; }
    public Guid? AssignedBy { get; private set; }
    public Guid? AssignedByRuleId { get; private set; }

    public BusinessLabel BusinessLabel { get; private set; } = null!;

    private DeviceBusinessLabel() { }

    public static DeviceBusinessLabel CreateManual(
        Guid tenantId,
        Guid deviceId,
        Guid businessLabelId,
        Guid? assignedBy)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));
        }
        if (businessLabelId == Guid.Empty)
        {
            throw new ArgumentException("BusinessLabelId is required.", nameof(businessLabelId));
        }

        return new DeviceBusinessLabel
        {
            TenantId = tenantId,
            DeviceId = deviceId,
            BusinessLabelId = businessLabelId,
            SourceType = ManualSourceType,
            SourceKey = ManualSourceKey,
            AssignedBy = assignedBy,
            AssignedAt = DateTimeOffset.UtcNow,
        };
    }

    public static DeviceBusinessLabel CreateRule(
        Guid tenantId,
        Guid deviceId,
        Guid businessLabelId,
        Guid ruleId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));
        }
        if (businessLabelId == Guid.Empty)
        {
            throw new ArgumentException("BusinessLabelId is required.", nameof(businessLabelId));
        }
        if (ruleId == Guid.Empty)
        {
            throw new ArgumentException("RuleId is required.", nameof(ruleId));
        }

        return new DeviceBusinessLabel
        {
            TenantId = tenantId,
            DeviceId = deviceId,
            BusinessLabelId = businessLabelId,
            SourceType = RuleSourceType,
            SourceKey = BuildRuleSourceKey(ruleId),
            AssignedByRuleId = ruleId,
            AssignedAt = DateTimeOffset.UtcNow,
        };
    }

    public static DeviceBusinessLabel Create(Guid tenantId, Guid deviceId, Guid businessLabelId) =>
        CreateManual(tenantId, deviceId, businessLabelId, assignedBy: null);

    public static string BuildRuleSourceKey(Guid ruleId) => ruleId.ToString("D");
}
