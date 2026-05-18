namespace PatchHound.Core.Interfaces;

public sealed record SoftwareStateMergeResult(
    int ProductCount,
    int ReleaseCount,
    int SourceIdentityCount,
    int ProductDeltaCount);

public interface IIngestionStateMerger
{
    Task<SoftwareStateMergeResult> MergeSoftwareAsync(
        Guid tenantId,
        Guid runId,
        CancellationToken ct);
}
