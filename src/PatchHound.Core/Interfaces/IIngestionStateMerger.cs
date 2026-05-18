namespace PatchHound.Core.Interfaces;

public sealed record SoftwareStateMergeResult(
    int ProductCount,
    int ReleaseCount,
    int SourceIdentityCount,
    int ProductDeltaCount);

public sealed record DeviceStateMergeResult(
    int DeviceCount,
    int DeviceDeltaCount);

public sealed record InstallationStateMergeResult(
    int InstallationCount,
    int InstallationDeltaCount);

public sealed record VulnerabilityStateMergeResult(
    int VulnerabilityCount,
    int VulnerabilityDeltaCount);

public interface IIngestionStateMerger
{
    Task<SoftwareStateMergeResult> MergeSoftwareAsync(
        Guid tenantId,
        Guid runId,
        CancellationToken ct);

    Task<DeviceStateMergeResult> MergeDevicesAsync(
        Guid tenantId,
        Guid runId,
        CancellationToken ct);

    Task<InstallationStateMergeResult> MergeInstallationsAsync(
        Guid tenantId,
        Guid runId,
        CancellationToken ct);

    Task<VulnerabilityStateMergeResult> MergeVulnerabilitiesAsync(
        Guid tenantId,
        Guid runId,
        CancellationToken ct);
}
