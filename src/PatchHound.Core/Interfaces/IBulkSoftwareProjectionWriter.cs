namespace PatchHound.Core.Interfaces;

/// <summary>
/// Bulk writer for the normalized software projection. Unlike the other bulk
/// writers, this seam takes NO row collection — the source of truth is the
/// already-persisted <c>InstalledSoftware</c> table; the implementation
/// expresses the projection (tenant rollup + per-device installation) as
/// set-based SQL.
/// </summary>
public interface IBulkSoftwareProjectionWriter
{
    /// <summary>
    /// Reconciles <c>SoftwareTenantRecords</c> for the given tenant/snapshot
    /// against the current <c>InstalledSoftware</c> set. Inserts new rows,
    /// extends observation windows on existing rows, and deletes stale rows
    /// whose product no longer has any installation.
    /// </summary>
    Task SyncTenantSoftwareAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct);

    /// <summary>
    /// Reconciles <c>SoftwareProductInstallations</c> for the given
    /// tenant/snapshot against the current <c>InstalledSoftware</c> set.
    /// Inserts new active rows, updates existing rows in place, and marks
    /// stale rows inactive (with <c>RemovedAt</c> set).
    /// </summary>
    Task SyncSoftwareInstallationsAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct);
}
