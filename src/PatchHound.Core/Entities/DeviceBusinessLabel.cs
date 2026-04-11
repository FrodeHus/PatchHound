namespace PatchHound.Core.Entities;

public class DeviceBusinessLabel
{
    public const string ManualSourceType = "Manual";
    public const string RuleSourceType = "Rule";
    public const string ManualSourceKey = "manual";

    public Guid Id { get; private set; }
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

    // Phase 1 canonical cleanup (Task 14): the minimal three-argument factory
    // remains for backward compatibility with callers and tests that seed
    // manual labels without an acting user. It delegates to CreateManual.
    public static DeviceBusinessLabel Create(Guid tenantId, Guid deviceId, Guid businessLabelId) =>
        CreateManual(tenantId, deviceId, businessLabelId, assignedBy: null);

    public static DeviceBusinessLabel CreateManual(
        Guid tenantId,
        Guid deviceId,
        Guid businessLabelId,
        Guid? assignedBy)
    {
        ValidateIds(tenantId, deviceId, businessLabelId);

        return new DeviceBusinessLabel
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            BusinessLabelId = businessLabelId,
            SourceType = ManualSourceType,
            SourceKey = ManualSourceKey,
            AssignedBy = assignedBy,
            AssignedByRuleId = null,
            AssignedAt = DateTimeOffset.UtcNow,
        };
    }

    public static DeviceBusinessLabel CreateRule(
        Guid tenantId,
        Guid deviceId,
        Guid businessLabelId,
        Guid ruleId)
    {
        ValidateIds(tenantId, deviceId, businessLabelId);
        if (ruleId == Guid.Empty)
        {
            throw new ArgumentException("RuleId is required.", nameof(ruleId));
        }

        return new DeviceBusinessLabel
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            BusinessLabelId = businessLabelId,
            SourceType = RuleSourceType,
            SourceKey = BuildRuleSourceKey(ruleId),
            AssignedBy = null,
            AssignedByRuleId = ruleId,
            AssignedAt = DateTimeOffset.UtcNow,
        };
    }

    public static string BuildRuleSourceKey(Guid ruleId) => ruleId.ToString("D");

    private static void ValidateIds(Guid tenantId, Guid deviceId, Guid businessLabelId)
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
    }
}
