using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IBulkExposureWriter
{
    Task<BulkExposureUpsertResult> UpsertAsync(
        IReadOnlyCollection<ExposureUpsertRow> rows,
        CancellationToken ct);

    /// <summary>
    /// Marks open exposures as missing when their LastSeenRunId is not the given
    /// run id, then resolves rows that have been missing for two distinct runs.
    /// Returns the number of rows resolved.
    /// </summary>
    Task<int> ResolveStaleAsync(Guid tenantId, Guid runId, DateTimeOffset resolvedAt, CancellationToken ct);
}
