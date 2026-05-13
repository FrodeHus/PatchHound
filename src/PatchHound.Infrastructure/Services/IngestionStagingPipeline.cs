using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;

namespace PatchHound.Infrastructure.Services;

/// <summary>
/// Handles the staging phase of the ingestion pipeline: writing raw vulnerability and
/// asset records into the staging tables, enqueuing enrichment jobs, and exposing
/// normalisation helpers that callers can reference before staging.
/// </summary>
public class IngestionStagingPipeline(
    PatchHoundDbContext dbContext,
    EnrichmentJobEnqueuer enrichmentJobEnqueuer,
    IngestionLeaseManager leaseManager,
    IngestionCheckpointWriter checkpointWriter
)
{
    internal const int AssetBatchSize = 200;
    internal const int VulnerabilityBatchSize = 250;

    // ── Vulnerability staging ────────────────────────────────────────────────

    internal async Task StageVulnerabilitiesAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IReadOnlyList<IngestionResult> results,
        int batchNumber,
        CancellationToken ct
    )
    {
        if (results.Count == 0)
        {
            return;
        }

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var stagedAt = DateTimeOffset.UtcNow;
        var rows = results.Select(result =>
            StagedVulnerability.Create(
                ingestionRunId,
                tenantId,
                normalizedSourceKey,
                result.ExternalId,
                result.Title,
                result.VendorSeverity,
                JsonSerializer.Serialize(result with { AffectedAssets = [] }, StagingSerializerOptions.Instance),
                stagedAt,
                batchNumber
            )
        );
        var exposures = results.SelectMany(result =>
            result.AffectedAssets.Select(affectedAsset =>
                StagedVulnerabilityExposure.Create(
                    ingestionRunId,
                    tenantId,
                    normalizedSourceKey,
                    result.ExternalId,
                    affectedAsset.ExternalAssetId,
                    affectedAsset.AssetName,
                    affectedAsset.AssetType,
                    JsonSerializer.Serialize(affectedAsset, StagingSerializerOptions.Instance),
                    stagedAt,
                    batchNumber
                )
            )
        );

        await dbContext.StagedVulnerabilities.AddRangeAsync(rows, ct);
        await dbContext.StagedVulnerabilityExposures.AddRangeAsync(exposures, ct);
        await dbContext.SaveChangesAsync(ct);
        dbContext.ChangeTracker.Clear();
    }

    internal async Task<int> StageVulnerabilityBatchesAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IVulnerabilityBatchSource batchSource,
        CancellationToken ct
    )
    {
        var checkpoint = await dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && item.Phase == CheckpointPhases.VulnerabilityStaging,
                ct
            );
        var batchNumber = checkpoint?.BatchNumber ?? 0;
        var cursorJson =
            string.IsNullOrWhiteSpace(checkpoint?.CursorJson) ? null : checkpoint.CursorJson;
        var totalResults = 0;

        while (true)
        {
            await leaseManager.ThrowIfAbortRequestedAsync(ingestionRunId, ct);
            batchNumber++;
            var batch = await batchSource.FetchVulnerabilityBatchAsync(
                tenantId,
                cursorJson,
                VulnerabilityBatchSize,
                ct
            );
            var normalizedResults = NormalizeResults(batch.Items);

            if (normalizedResults.Count > 0)
            {
                totalResults += normalizedResults.Count;
                await StageVulnerabilitiesAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    normalizedResults,
                    batchNumber,
                    ct
                );
                await checkpointWriter.CommitCheckpointAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    CheckpointPhases.VulnerabilityStaging,
                    batchNumber,
                    batch.NextCursorJson,
                    normalizedResults.Count,
                    batch.IsComplete ? CheckpointStatuses.Completed : CheckpointStatuses.Running,
                    ct
                );
            }
            else
            {
                await checkpointWriter.CommitCheckpointAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    CheckpointPhases.VulnerabilityStaging,
                    batchNumber,
                    batch.NextCursorJson,
                    0,
                    batch.IsComplete ? CheckpointStatuses.Completed : CheckpointStatuses.Running,
                    ct
                );
            }

            if (batch.IsComplete)
            {
                break;
            }

            cursorJson = batch.NextCursorJson;
        }

        return totalResults;
    }

    // ── Asset staging ────────────────────────────────────────────────────────

    internal async Task StageAssetInventorySnapshotAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IngestionAssetInventorySnapshot snapshot,
        int batchNumber,
        CancellationToken ct
    )
    {
        if (snapshot.Assets.Count == 0 && snapshot.DeviceSoftwareLinks.Count == 0)
        {
            return;
        }

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var stagedAt = DateTimeOffset.UtcNow;

        if (snapshot.Assets.Count > 0)
        {
            var stagedDeviceRecords = snapshot.Assets.Select(asset =>
                StagedDevice.Create(
                    ingestionRunId,
                    tenantId,
                    normalizedSourceKey,
                    asset.ExternalId,
                    asset.Name,
                    asset.AssetType,
                    JsonSerializer.Serialize(asset, StagingSerializerOptions.Instance),
                    stagedAt,
                    batchNumber
                )
            );
            await dbContext.StagedDevices.AddRangeAsync(stagedDeviceRecords, ct);
        }

        if (snapshot.DeviceSoftwareLinks.Count > 0)
        {
            var stagedLinks = snapshot.DeviceSoftwareLinks.Select(link =>
                StagedDeviceSoftwareInstallation.Create(
                    ingestionRunId,
                    tenantId,
                    normalizedSourceKey,
                    link.DeviceExternalId,
                    link.SoftwareExternalId,
                    link.ObservedAt,
                    JsonSerializer.Serialize(link, StagingSerializerOptions.Instance),
                    stagedAt,
                    batchNumber
                )
            );
            await dbContext.StagedDeviceSoftwareInstallations.AddRangeAsync(stagedLinks, ct);
        }

        await dbContext.SaveChangesAsync(ct);
        dbContext.ChangeTracker.Clear();
    }

    internal async Task<AssetBatchStageSummary> StageAssetBatchesAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IAssetInventoryBatchSource batchSource,
        CancellationToken ct
    )
    {
        var checkpoint = await dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item =>
                item.IngestionRunId == ingestionRunId
                && item.TenantId == tenantId
                && item.SourceKey == sourceKey
                && item.Phase == CheckpointPhases.AssetStaging,
                ct
            );

        var batchNumber = checkpoint?.BatchNumber ?? 0;
        var cursorJson = string.IsNullOrWhiteSpace(checkpoint?.CursorJson)
            ? null
            : checkpoint!.CursorJson;
        var totalAssets = 0;
        var totalSoftware = 0;
        var totalLinks = 0;
        var totalSoftwareWithoutMachineReferences = 0;

        while (true)
        {
            await leaseManager.ThrowIfAbortRequestedAsync(ingestionRunId, ct);
            var batch = await batchSource.FetchAssetBatchAsync(
                tenantId,
                cursorJson,
                AssetBatchSize,
                ct
            );
            batchNumber++;

            var normalizedBatch = NormalizeAssetSnapshots(batch.Items);
            if (
                normalizedBatch.Assets.Count > 0
                || normalizedBatch.DeviceSoftwareLinks.Count > 0
                || normalizedBatch.RetrievedSoftwareCount > 0
                || normalizedBatch.SoftwareWithoutMachineReferencesCount > 0
            )
            {
                totalAssets += normalizedBatch.Assets.Count;
                totalSoftwareWithoutMachineReferences +=
                    normalizedBatch.SoftwareWithoutMachineReferencesCount;

                await StageAssetInventorySnapshotAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    normalizedBatch,
                    batchNumber,
                    ct
                );
                await checkpointWriter.CommitCheckpointAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    CheckpointPhases.AssetStaging,
                    batchNumber,
                    batch.NextCursorJson,
                    normalizedBatch.Assets.Count + normalizedBatch.DeviceSoftwareLinks.Count,
                    batch.IsComplete ? CheckpointStatuses.Completed : CheckpointStatuses.Running,
                    ct
                );
            }
            else if (batch.IsComplete)
            {
                await checkpointWriter.CommitCheckpointAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    CheckpointPhases.AssetStaging,
                    batchNumber,
                    batch.NextCursorJson,
                    0,
                    CheckpointStatuses.Completed,
                    ct
                );
            }

            if (batch.IsComplete)
            {
                break;
            }

            cursorJson = batch.NextCursorJson;
        }

        totalSoftware = await dbContext
            .StagedDevices.IgnoreQueryFilters()
            .Where(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && EF.Functions.Like(item.ExternalId, "defender-sw::%")
            )
            .Select(item => item.ExternalId)
            .Distinct()
            .CountAsync(
                ct
            );
        totalLinks = await dbContext
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .Select(item => new { item.DeviceExternalId, item.SoftwareExternalId })
            .Distinct()
            .CountAsync(ct);

        return new AssetBatchStageSummary(
            totalAssets,
            totalSoftware,
            totalLinks,
            totalSoftwareWithoutMachineReferences,
            batchNumber
        );
    }

    // ── Enrichment job enqueueing ─────────────────────────────────────────────

    internal async Task EnqueueEnrichmentJobsForRunAsync(
        Guid ingestionRunId,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var externalIds = await dbContext
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .Select(item => item.ExternalId)
            .Distinct()
            .ToListAsync(ct);

        if (externalIds.Count == 0)
        {
            return;
        }

        var vulnerabilityIds = await dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .Where(v => externalIds.Contains(v.ExternalId))
            .Select(v => v.Id)
            .Distinct()
            .ToListAsync(ct);

        await enrichmentJobEnqueuer.EnqueueVulnerabilityJobsAsync(
            tenantId,
            vulnerabilityIds,
            ct
        );

        var normalizedSoftwareIds = await dbContext
            .SoftwareTenantRecords.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ts => ts.TenantId == tenantId)
            .Select(ts => ts.SoftwareProductId)
            .Distinct()
            .ToListAsync(ct);

        if (normalizedSoftwareIds.Count > 0)
        {
            await enrichmentJobEnqueuer.EnqueueSoftwareEndOfLifeJobsAsync(
                tenantId,
                normalizedSoftwareIds,
                ct
            );
            await enrichmentJobEnqueuer.EnqueueSoftwareSupplyChainJobsAsync(
                tenantId,
                normalizedSoftwareIds,
                ct
            );
        }
    }

    // ── Static normalisation helpers ─────────────────────────────────────────

    internal static IReadOnlyList<IngestionResult> NormalizeResults(
        IReadOnlyList<IngestionResult> results
    )
    {
        return results
            .GroupBy(result => result.ExternalId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var affectedAssets = group
                    .SelectMany(item => item.AffectedAssets)
                    .GroupBy(asset => asset.ExternalAssetId, StringComparer.OrdinalIgnoreCase)
                    .Select(assetGroup => assetGroup.First())
                    .ToList();
                var references = group
                    .SelectMany(item => item.References ?? [])
                    .GroupBy(reference => reference.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(referenceGroup => referenceGroup.First())
                    .ToList();
                var affectedSoftware = group
                    .SelectMany(item => item.AffectedSoftware ?? [])
                    .GroupBy(
                        item =>
                            $"{item.Criteria}|{item.VersionStartIncluding}|{item.VersionStartExcluding}|{item.VersionEndIncluding}|{item.VersionEndExcluding}|{item.Vulnerable}",
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Select(softwareGroup => softwareGroup.First())
                    .ToList();
                var sources = group
                    .SelectMany(item => item.Sources ?? [])
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return first with
                {
                    AffectedAssets = affectedAssets,
                    References = references,
                    AffectedSoftware = affectedSoftware,
                    Sources = sources,
                };
            })
            .ToList();
    }

    internal static IngestionAssetInventorySnapshot NormalizeAssetSnapshot(
        IngestionAssetInventorySnapshot snapshot
    )
    {
        var assets = snapshot
            .Assets.GroupBy(asset => asset.ExternalId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        var deviceSoftwareLinks = snapshot
            .DeviceSoftwareLinks.GroupBy(
                link => $"{link.DeviceExternalId}:{link.SoftwareExternalId}",
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.OrderByDescending(link => link.ObservedAt).First())
            .ToList();

        return new IngestionAssetInventorySnapshot(
            assets,
            deviceSoftwareLinks,
            snapshot.RetrievedSoftwareCount,
            snapshot.SoftwareWithoutMachineReferencesCount
        );
    }

    internal static IngestionAssetInventorySnapshot NormalizeAssetSnapshots(
        IReadOnlyList<IngestionAssetInventorySnapshot> snapshots
    )
    {
        if (snapshots.Count == 0)
        {
            return new IngestionAssetInventorySnapshot([], []);
        }

        return NormalizeAssetSnapshot(
            new IngestionAssetInventorySnapshot(
                snapshots.SelectMany(item => item.Assets).ToList(),
                snapshots.SelectMany(item => item.DeviceSoftwareLinks).ToList(),
                snapshots.Sum(item => item.RetrievedSoftwareCount),
                snapshots.Sum(item => item.SoftwareWithoutMachineReferencesCount)
            )
        );
    }

    // ── Static chunking utility ───────────────────────────────────────────────

    internal static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (var index = 0; index < items.Count; index += size)
        {
            var count = Math.Min(size, items.Count - index);
            var chunk = new List<T>(count);
            for (var offset = 0; offset < count; offset++)
            {
                chunk.Add(items[index + offset]);
            }

            yield return chunk;
        }
    }
}

/// <summary>
/// Accumulates counts from a multi-batch asset staging pass.
/// </summary>
internal sealed record AssetBatchStageSummary(
    int AssetCount,
    int SoftwareCount,
    int LinkCount,
    int SoftwareWithoutMachineReferencesCount,
    int BatchNumber
);
