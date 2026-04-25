using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class NormalizedSoftwareProjectionService(PatchHoundDbContext dbContext)
{
    public async Task SyncTenantAsync(Guid tenantId, CancellationToken ct)
        => await SyncTenantAsync(tenantId, null, ct);

    public async Task SyncTenantAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        await UpsertTenantSoftwareAsync(tenantId, snapshotId, ct);
        await dbContext.SaveChangesAsync(ct);
        await UpsertSoftwareInstallationsAsync(tenantId, snapshotId, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task UpsertTenantSoftwareAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        var softwareGroups = await dbContext.InstalledSoftware
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .GroupBy(i => i.SoftwareProductId)
            .Select(g => new
            {
                SoftwareProductId = g.Key,
                FirstSeenAt = g.Min(i => i.FirstSeenAt),
                LastSeenAt = g.Max(i => i.LastSeenAt),
            })
            .ToListAsync(ct);

        var softwareProductIdSet = softwareGroups
            .Select(g => g.SoftwareProductId)
            .ToHashSet();

        var existingRows = await dbContext.SoftwareTenantRecords
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SnapshotId == snapshotId)
            .ToListAsync(ct);

        var existingBySoftwareProductId = existingRows.ToDictionary(r => r.SoftwareProductId);

        foreach (var group in softwareGroups)
        {
            if (!existingBySoftwareProductId.TryGetValue(group.SoftwareProductId, out var row))
            {
                row = SoftwareTenantRecord.Create(
                    tenantId, snapshotId, group.SoftwareProductId,
                    group.FirstSeenAt, group.LastSeenAt);
                await dbContext.SoftwareTenantRecords.AddAsync(row, ct);
            }
            else
            {
                var firstSeen = row.FirstSeenAt < group.FirstSeenAt ? row.FirstSeenAt : group.FirstSeenAt;
                row.UpdateObservationWindow(firstSeen, group.LastSeenAt);
            }
        }

        var staleRows = existingRows
            .Where(r => !softwareProductIdSet.Contains(r.SoftwareProductId))
            .ToList();
        if (staleRows.Count > 0)
            dbContext.SoftwareTenantRecords.RemoveRange(staleRows);
    }

    private async Task UpsertSoftwareInstallationsAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        var tenantSoftwareByProductId = await dbContext.SoftwareTenantRecords
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SnapshotId == snapshotId)
            .Select(item => new { item.SoftwareProductId, item.Id })
            .ToDictionaryAsync(item => item.SoftwareProductId, item => item.Id, ct);

        var installRows = await dbContext.InstalledSoftware
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Select(item => new
            {
                item.Id,
                item.DeviceId,
                item.SoftwareProductId,
                item.SourceSystemId,
                item.Version,
                item.FirstSeenAt,
                item.LastSeenAt,
                SourceKey = dbContext.SourceSystems
                    .Where(source => source.Id == item.SourceSystemId)
                    .Select(source => source.Key)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var activeInstallIds = installRows.Select(item => item.Id).ToHashSet();
        var existingRows = await dbContext.SoftwareProductInstallations
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SnapshotId == snapshotId)
            .ToListAsync(ct);
        var existingByInstalledSoftwareId = existingRows.ToDictionary(item => item.SoftwareAssetId);

        foreach (var install in installRows)
        {
            if (!tenantSoftwareByProductId.TryGetValue(install.SoftwareProductId, out var tenantSoftwareId))
            {
                continue;
            }

            var sourceSystem = ResolveSourceSystem(install.SourceKey);
            if (!existingByInstalledSoftwareId.TryGetValue(install.Id, out var row))
            {
                row = SoftwareProductInstallation.Create(
                    tenantId,
                    snapshotId,
                    tenantSoftwareId,
                    softwareAssetId: install.Id,
                    deviceAssetId: install.DeviceId,
                    sourceSystem,
                    install.Version,
                    install.FirstSeenAt,
                    install.LastSeenAt,
                    removedAt: null,
                    isActive: true,
                    currentEpisodeNumber: 1);
                await dbContext.SoftwareProductInstallations.AddAsync(row, ct);
                continue;
            }

            row.UpdateProjection(
                snapshotId,
                tenantSoftwareId,
                sourceSystem,
                install.Version,
                install.FirstSeenAt,
                install.LastSeenAt,
                removedAt: null,
                isActive: true,
                currentEpisodeNumber: Math.Max(1, row.CurrentEpisodeNumber));
        }

        foreach (var staleRow in existingRows.Where(item => !activeInstallIds.Contains(item.SoftwareAssetId)))
        {
            staleRow.UpdateProjection(
                snapshotId,
                staleRow.TenantSoftwareId,
                staleRow.SourceSystem,
                staleRow.DetectedVersion,
                staleRow.FirstSeenAt,
                staleRow.LastSeenAt,
                removedAt: DateTimeOffset.UtcNow,
                isActive: false,
                currentEpisodeNumber: staleRow.CurrentEpisodeNumber);
        }
    }

    private static SoftwareIdentitySourceSystem ResolveSourceSystem(string? sourceKey)
    {
        return string.Equals(sourceKey, "authenticated-scan", StringComparison.OrdinalIgnoreCase)
            ? SoftwareIdentitySourceSystem.AuthenticatedScan
            : SoftwareIdentitySourceSystem.Defender;
    }
}
