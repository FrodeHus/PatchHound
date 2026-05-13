using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class IngestionSnapshotLifecycle
{
    private readonly PatchHoundDbContext dbContext;
    private readonly IIngestionBulkWriter _bulkWriter;

    public IngestionSnapshotLifecycle(PatchHoundDbContext dbContext)
        : this(dbContext, CreateBulkWriter(dbContext)) { }

    internal IngestionSnapshotLifecycle(PatchHoundDbContext dbContext, IIngestionBulkWriter bulkWriter)
    {
        this.dbContext = dbContext;
        _bulkWriter = bulkWriter;
    }

    private static IIngestionBulkWriter CreateBulkWriter(PatchHoundDbContext db) =>
        db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? new InMemoryIngestionBulkWriter(db)
            : new PostgresIngestionBulkWriter(db);

    internal static bool SupportsSoftwareSnapshots(string sourceKey)
    {
        return sourceKey.Trim().ToLowerInvariant() == TenantSourceCatalog.DefenderSourceKey;
    }

    internal async Task<IngestionSnapshot> GetOrCreateBuildingSoftwareSnapshotAsync(
        Guid tenantId,
        string sourceKey,
        Guid ingestionRunId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var source = await dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .FirstAsync(
                item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                ct
            );

        if (source.BuildingSnapshotId is Guid buildingSnapshotId)
        {
            var existing = await dbContext
                .IngestionSnapshots.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == buildingSnapshotId, ct);
            if (
                existing is not null
                && existing.IngestionRunId == ingestionRunId
                && existing.Status == IngestionSnapshotStatuses.Building
            )
            {
                return existing;
            }

            if (existing is not null && existing.Status == IngestionSnapshotStatuses.Building)
            {
                existing.Discard();
                await CleanupSnapshotDataAsync(existing.Id, ct);
            }
        }

        var snapshot = IngestionSnapshot.Create(
            tenantId,
            normalizedSourceKey,
            ingestionRunId,
            DateTimeOffset.UtcNow
        );
        source.SetSnapshotPointers(source.ActiveSnapshotId, snapshot.Id);
        await dbContext.IngestionSnapshots.AddAsync(snapshot, ct);
        await dbContext.SaveChangesAsync(ct);
        return snapshot;
    }

    internal async Task PublishSnapshotAsync(
        Guid tenantId,
        string sourceKey,
        Guid snapshotId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var source = await dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .FirstAsync(
                item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                ct
            );
        var snapshot = await dbContext
            .IngestionSnapshots.IgnoreQueryFilters()
            .FirstAsync(item => item.Id == snapshotId, ct);

        Guid? retiredSnapshotId = null;
        if (source.ActiveSnapshotId is Guid previousActiveSnapshotId && previousActiveSnapshotId != snapshotId)
        {
            var previous = await dbContext
                .IngestionSnapshots.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == previousActiveSnapshotId, ct);
            if (previous is not null)
            {
                previous.Discard();
                retiredSnapshotId = previous.Id;
            }
        }

        snapshot.MarkPublished();
        source.SetSnapshotPointers(snapshot.Id, null);
        await dbContext.SaveChangesAsync(ct);

        if (retiredSnapshotId.HasValue)
        {
            // RemediationCase is keyed by (TenantId, SoftwareProductId) and stable across snapshot
            // rotations — no re-keying of downstream entities is required when the active snapshot changes.
            await CleanupSnapshotDataAsync(retiredSnapshotId.Value, ct);
        }
    }

    internal async Task DiscardBuildingSnapshotAsync(
        Guid tenantId,
        string sourceKey,
        Guid snapshotId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var source = await dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                ct
            );
        var snapshot = await dbContext
            .IngestionSnapshots.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == snapshotId, ct);

        if (snapshot is not null && snapshot.Status == IngestionSnapshotStatuses.Building)
        {
            snapshot.Discard();
        }

        if (source is not null && source.BuildingSnapshotId == snapshotId)
        {
            source.SetSnapshotPointers(source.ActiveSnapshotId, null);
        }

        await dbContext.SaveChangesAsync(ct);
        await CleanupSnapshotDataAsync(snapshotId, ct);
    }

    internal async Task CleanupSnapshotDataAsync(Guid snapshotId, CancellationToken ct)
    {
        await _bulkWriter.CleanupSnapshotDataAsync(snapshotId, ct);
    }
}
