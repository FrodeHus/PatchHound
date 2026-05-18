namespace PatchHound.Core.Interfaces;

public sealed record DirectExposureMergeResult(
    int ExposureCount,
    int ResolvedCount);

public interface IExposureStateMerger
{
    Task<DirectExposureMergeResult> MergeDirectExposuresAsync(
        Guid tenantId,
        Guid runId,
        CancellationToken ct);
}
