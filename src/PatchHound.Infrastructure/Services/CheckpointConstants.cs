namespace PatchHound.Infrastructure.Services;

internal static class CheckpointPhases
{
    public const string AssetStaging = "asset-staging";
    public const string AssetMerge = "asset-merge";
    public const string VulnerabilityStaging = "vulnerability-staging";
    public const string VulnerabilityMerge = "vulnerability-merge";
    public const string CloudAppStaging = "cloud-app-staging";
}

internal static class CheckpointStatuses
{
    public const string Running = "Running";
    public const string Staged = "Staged";
    public const string Completed = "Completed";
}
