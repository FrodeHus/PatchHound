using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
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
}
