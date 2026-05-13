using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class IngestionCheckpointWriter(PatchHoundDbContext dbContext)
{
    public async Task<bool> IsCheckpointCompletedAsync(
        Guid ingestionRunId,
        string phase,
        CancellationToken ct
    )
    {
        return await dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .AnyAsync(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && item.Phase == phase
                    && item.Status == CheckpointStatuses.Completed,
                ct
            );
    }

    public async Task<int> GetCheckpointBatchNumberAsync(
        Guid ingestionRunId,
        string phase,
        CancellationToken ct
    )
    {
        return await dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId && item.Phase == phase)
            .Select(item => item.BatchNumber)
            .FirstOrDefaultAsync(ct);
    }

    public async Task CommitCheckpointAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        string phase,
        int batchNumber,
        string? cursorJson,
        int recordsCommitted,
        string status,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var checkpoint = await dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.IngestionRunId == ingestionRunId && item.Phase == phase,
                ct
            );

        if (checkpoint is null)
        {
            checkpoint = IngestionCheckpoint.Start(
                ingestionRunId,
                tenantId,
                normalizedSourceKey,
                phase,
                DateTimeOffset.UtcNow
            );
            await dbContext.IngestionCheckpoints.AddAsync(checkpoint, ct);
        }

        checkpoint.CommitBatch(
            batchNumber,
            cursorJson,
            recordsCommitted,
            status,
            DateTimeOffset.UtcNow
        );

        await dbContext.SaveChangesAsync(ct);
        dbContext.ChangeTracker.Clear();
    }
}
