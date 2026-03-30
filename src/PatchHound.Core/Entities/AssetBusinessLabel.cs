namespace PatchHound.Core.Entities;

public class AssetBusinessLabel
{
    public const string ManualSourceType = "Manual";
    public const string RuleSourceType = "Rule";
    public const string ManualSourceKey = "manual";

    public Guid AssetId { get; private set; }
    public Guid BusinessLabelId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string SourceKey { get; private set; } = null!;
    public DateTimeOffset AssignedAt { get; private set; }
    public Guid? AssignedBy { get; private set; }
    public Guid? AssignedByRuleId { get; private set; }

    public Asset Asset { get; private set; } = null!;
    public BusinessLabel BusinessLabel { get; private set; } = null!;

    private AssetBusinessLabel() { }

    public static AssetBusinessLabel CreateManual(Guid assetId, Guid businessLabelId, Guid? assignedBy)
    {
        return new AssetBusinessLabel
        {
            AssetId = assetId,
            BusinessLabelId = businessLabelId,
            SourceType = ManualSourceType,
            SourceKey = ManualSourceKey,
            AssignedBy = assignedBy,
            AssignedAt = DateTimeOffset.UtcNow,
        };
    }

    public static AssetBusinessLabel CreateRule(Guid assetId, Guid businessLabelId, Guid ruleId)
    {
        return new AssetBusinessLabel
        {
            AssetId = assetId,
            BusinessLabelId = businessLabelId,
            SourceType = RuleSourceType,
            SourceKey = BuildRuleSourceKey(ruleId),
            AssignedByRuleId = ruleId,
            AssignedAt = DateTimeOffset.UtcNow,
        };
    }

    public static string BuildRuleSourceKey(Guid ruleId) => ruleId.ToString("D");
}
