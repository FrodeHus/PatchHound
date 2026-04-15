using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class NormalizedSoftwareProjectionService(
    PatchHoundDbContext dbContext,
    NormalizedSoftwareResolver resolver
)
{
    public async Task SyncTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await SyncTenantAsync(tenantId, null, ct);
    }

    public async Task SyncTenantAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        var resolutions = await resolver.SyncTenantAsync(tenantId, ct);
        await RebuildInstallationProjectionAsync(tenantId, snapshotId, resolutions, ct);
        await dbContext.SaveChangesAsync(ct);
        await RebuildVulnerabilityProjectionAsync(tenantId, snapshotId, resolutions, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task RebuildInstallationProjectionAsync(
        Guid tenantId,
        Guid? snapshotId,
        IReadOnlyDictionary<Guid, NormalizedSoftwareResolver.ResolutionResult> resolutions,
        CancellationToken ct
    )
    {
        var tenantSoftwareRows = await UpsertTenantSoftwareAsync(tenantId, snapshotId, resolutions, ct);

        var existingInstallations = await dbContext
            .SoftwareProductInstallations.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SnapshotId == snapshotId)
            .ToListAsync(ct);
        if (existingInstallations.Count > 0)
        {
            dbContext.SoftwareProductInstallations.RemoveRange(existingInstallations);
        }

        if (resolutions.Count == 0)
        {
            return;
        }

        var relevantSoftwareAssetIds = resolutions.Keys.ToList();

        var currentInstallations = await dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && relevantSoftwareAssetIds.Contains(item.SoftwareAssetId)
            )
            .ToListAsync(ct);
        var currentInstallationsByPair = currentInstallations.ToDictionary(
            item => BuildPairKey(item.DeviceAssetId, item.SoftwareAssetId),
            StringComparer.Ordinal
        );

        var latestEpisodes = await dbContext
            .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && relevantSoftwareAssetIds.Contains(item.SoftwareAssetId)
            )
            .GroupBy(item => new { item.DeviceAssetId, item.SoftwareAssetId })
            .Select(group => group
                .OrderByDescending(item => item.EpisodeNumber)
                .First())
            .ToListAsync(ct);

        var rows = latestEpisodes
            .Where(episode => resolutions.ContainsKey(episode.SoftwareAssetId))
            .Select(episode =>
            {
                var key = BuildPairKey(episode.DeviceAssetId, episode.SoftwareAssetId);
                currentInstallationsByPair.TryGetValue(key, out var currentInstallation);
                var resolution = resolutions[episode.SoftwareAssetId];
                var tenantSoftware = tenantSoftwareRows[resolution.SoftwareProductId];

                return SoftwareProductInstallation.Create(
                    tenantId,
                    snapshotId,
                    tenantSoftware.Id,
                    episode.SoftwareAssetId,
                    episode.DeviceAssetId,
                    resolution.SourceSystem,
                    resolution.DetectedVersion,
                    episode.FirstSeenAt,
                    currentInstallation?.LastSeenAt ?? episode.LastSeenAt,
                    currentInstallation is null ? episode.RemovedAt : null,
                    currentInstallation is not null,
                    episode.EpisodeNumber
                );
            })
            .ToList();

        if (rows.Count > 0)
        {
            await dbContext.SoftwareProductInstallations.AddRangeAsync(rows, ct);
        }
    }

    private async Task RebuildVulnerabilityProjectionAsync(
        Guid tenantId,
        Guid? snapshotId,
        IReadOnlyDictionary<Guid, NormalizedSoftwareResolver.ResolutionResult> resolutions,
        CancellationToken ct
    )
    {
        // Phase-2: SoftwareVulnerabilityMatch deleted. No-op — projections will be rebuilt in Phase 3.
        await Task.CompletedTask;
    }

    private async Task<Dictionary<Guid, SoftwareTenantRecord>> UpsertTenantSoftwareAsync(
        Guid tenantId,
        Guid? snapshotId,
        IReadOnlyDictionary<Guid, NormalizedSoftwareResolver.ResolutionResult> resolutions,
        CancellationToken ct
    )
    {
        var softwareProductIds = resolutions
            .Values.Select(item => item.SoftwareProductId)
            .Distinct()
            .ToList();

        var existingRows = await dbContext
            .SoftwareTenantRecords.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SnapshotId == snapshotId)
            .ToListAsync(ct);

        var existingBySoftwareProductId = existingRows.ToDictionary(item => item.SoftwareProductId);
        var rowsBySoftwareProductId = new Dictionary<Guid, SoftwareTenantRecord>();
        var now = DateTimeOffset.UtcNow;

        foreach (var softwareProductId in softwareProductIds)
        {
            if (!existingBySoftwareProductId.TryGetValue(softwareProductId, out var row))
            {
                row = SoftwareTenantRecord.Create(tenantId, snapshotId, softwareProductId, now, now);
                await dbContext.SoftwareTenantRecords.AddAsync(row, ct);
            }
            else
            {
                row.AssignSnapshot(snapshotId);
                row.UpdateObservationWindow(row.FirstSeenAt, now);
            }

            rowsBySoftwareProductId[softwareProductId] = row;
        }

        var staleRows = existingRows
            .Where(item => !softwareProductIds.Contains(item.SoftwareProductId))
            .ToList();
        if (staleRows.Count > 0)
        {
            dbContext.SoftwareTenantRecords.RemoveRange(staleRows);
        }

        await dbContext.SaveChangesAsync(ct);
        return rowsBySoftwareProductId;
    }

    private static string BuildPairKey(Guid deviceAssetId, Guid softwareAssetId)
    {
        return $"{deviceAssetId:N}:{softwareAssetId:N}";
    }

}
