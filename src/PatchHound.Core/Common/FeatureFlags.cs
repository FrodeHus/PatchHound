using PatchHound.Core.Enums;

namespace PatchHound.Core.Common;

public record FeatureFlagMeta(string DisplayName, FeatureFlagStage Stage);

public static class FeatureFlags
{
    public const string Workflows = "Workflows";
    public const string AuthenticatedScans = "AuthenticatedScans";

    public static readonly IReadOnlyDictionary<string, FeatureFlagMeta> Metadata =
        new Dictionary<string, FeatureFlagMeta>
        {
            [Workflows] = new("Workflow engine", FeatureFlagStage.GenerallyAvailable),
            [AuthenticatedScans] = new("Authenticated scanning", FeatureFlagStage.GenerallyAvailable),
        };
}
