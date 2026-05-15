using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IBulkDeviceMergeWriter
{
    /// <summary>
    /// Upserts devices via temp-table + INSERT ... ON CONFLICT. Returns the
    /// canonical device IDs keyed by (SourceSystemId, ExternalId) so callers
    /// can resolve IDs for the subsequent installed-software pass.
    /// </summary>
    Task<IReadOnlyDictionary<(Guid SourceSystemId, string ExternalId), Guid>>
        UpsertDevicesAsync(IReadOnlyCollection<DeviceMergeRow> rows, CancellationToken ct);

    /// <summary>
    /// Upserts <see cref="PatchHound.Core.Entities.InstalledSoftware"/> rows. New rows
    /// are inserted; existing rows have their <c>LastSeenAt</c> advanced. Returns the
    /// total number of rows touched (inserted + updated).
    /// </summary>
    Task<int> UpsertInstalledSoftwareAsync(
        IReadOnlyCollection<InstalledSoftwareMergeRow> rows,
        CancellationToken ct);
}
