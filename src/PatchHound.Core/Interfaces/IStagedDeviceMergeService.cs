namespace PatchHound.Core.Interfaces;

public record StagedDeviceMergeSummary(
    int DevicesCreated,
    int DevicesTouched,
    int InstalledSoftwareCreated,
    int InstalledSoftwareTouched,
    int DevicesSkipped = 0,
    int DevicesDeactivated = 0);

public interface IStagedDeviceMergeService
{
    Task<StagedDeviceMergeSummary> MergeAsync(Guid ingestionRunId, Guid tenantId, CancellationToken ct);
}
