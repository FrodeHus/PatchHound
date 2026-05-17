using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IBulkExposureWriter
{
    Task<BulkExposureUpsertResult> UpsertAsync(
        IReadOnlyCollection<ExposureUpsertRow> rows,
        CancellationToken ct);

    /// <summary>
    /// Resolves exposures for the given tenant whose LastSeenRunId is not the
    /// given run id and whose status is Open. Returns the number of rows resolved.
    /// </summary>
    Task<int> ResolveStaleAsync(Guid tenantId, Guid runId, DateTimeOffset resolvedAt, CancellationToken ct);
}
