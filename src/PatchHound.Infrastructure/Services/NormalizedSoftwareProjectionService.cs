using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Services;

/// <summary>
/// Thin orchestrator around <see cref="IBulkSoftwareProjectionWriter"/>. The
/// actual set-based SQL for projecting <c>InstalledSoftware</c> into the
/// normalized <c>SoftwareTenantRecords</c> and <c>SoftwareProductInstallations</c>
/// projection tables lives in the writer implementation.
/// </summary>
public class NormalizedSoftwareProjectionService(IBulkSoftwareProjectionWriter writer)
{
    public Task SyncTenantAsync(Guid tenantId, CancellationToken ct)
        => SyncTenantAsync(tenantId, null, ct);

    public async Task SyncTenantAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        await writer.SyncTenantSoftwareAsync(tenantId, snapshotId, ct);
        await writer.SyncSoftwareInstallationsAsync(tenantId, snapshotId, ct);
    }
}
