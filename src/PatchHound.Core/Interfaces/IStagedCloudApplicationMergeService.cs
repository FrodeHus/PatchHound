namespace PatchHound.Core.Interfaces;

public record StagedCloudApplicationMergeSummary(
    int ApplicationsCreated,
    int ApplicationsTouched,
    int ApplicationsDeactivated
);

public interface IStagedCloudApplicationMergeService
{
    Task<StagedCloudApplicationMergeSummary> MergeAsync(Guid ingestionRunId, Guid tenantId, CancellationToken ct);
}
