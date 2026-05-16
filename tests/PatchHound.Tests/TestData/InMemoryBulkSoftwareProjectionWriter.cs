using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.TestData;

/// <summary>
/// In-memory replacement for <see cref="IBulkSoftwareProjectionWriter"/> used
/// by tests that run against the EF Core InMemory provider (which cannot
/// execute the production Postgres SQL writer).
///
/// SEMANTIC DIVERGENCE vs production <c>PostgresBulkSoftwareProjectionWriter</c>:
/// - Uses EF change-tracking + <c>SoftwareTenantRecord.Create</c> /
///   <c>SoftwareProductInstallation.Create</c> / <c>UpdateProjection</c> to
///   mimic the SQL upsert + stale-row reconciliation. Mirrors the behavior of
///   the original <c>NormalizedSoftwareProjectionService</c> before it was
///   refactored to delegate to the writer.
/// </summary>
internal sealed class InMemoryBulkSoftwareProjectionWriter(PatchHoundDbContext db)
    : IBulkSoftwareProjectionWriter
{
    public async Task SyncTenantSoftwareAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        var groups = await db.InstalledSoftware
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

        var productIds = groups.Select(g => g.SoftwareProductId).ToHashSet();

        var existing = await db.SoftwareTenantRecords
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.SnapshotId == snapshotId)
            .ToListAsync(ct);
        var existingByProductId = existing.ToDictionary(r => r.SoftwareProductId);

        foreach (var g in groups)
        {
            if (existingByProductId.TryGetValue(g.SoftwareProductId, out var row))
            {
                var firstSeen = row.FirstSeenAt < g.FirstSeenAt ? row.FirstSeenAt : g.FirstSeenAt;
                var lastSeen = row.LastSeenAt > g.LastSeenAt ? row.LastSeenAt : g.LastSeenAt;
                row.UpdateObservationWindow(firstSeen, lastSeen);
            }
            else
            {
                var fresh = SoftwareTenantRecord.Create(
                    tenantId, snapshotId, g.SoftwareProductId, g.FirstSeenAt, g.LastSeenAt);
                await db.SoftwareTenantRecords.AddAsync(fresh, ct);
            }
        }

        var stale = existing.Where(r => !productIds.Contains(r.SoftwareProductId)).ToList();
        if (stale.Count > 0)
            db.SoftwareTenantRecords.RemoveRange(stale);

        await db.SaveChangesAsync(ct);
    }

    public async Task SyncSoftwareInstallationsAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        var tenantSoftwareByProductId = await db.SoftwareTenantRecords
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.SnapshotId == snapshotId)
            .Select(r => new { r.SoftwareProductId, r.Id })
            .ToDictionaryAsync(r => r.SoftwareProductId, r => r.Id, ct);

        var installs = await db.InstalledSoftware
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .Select(i => new
            {
                i.Id,
                i.DeviceId,
                i.SoftwareProductId,
                i.SourceSystemId,
                i.Version,
                i.FirstSeenAt,
                i.LastSeenAt,
                SourceKey = db.SourceSystems
                    .Where(s => s.Id == i.SourceSystemId)
                    .Select(s => s.Key)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var activeInstallIds = installs.Select(i => i.Id).ToHashSet();
        var existing = await db.SoftwareProductInstallations
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.SnapshotId == snapshotId)
            .ToListAsync(ct);
        var existingByAssetId = existing.ToDictionary(p => p.SoftwareAssetId);

        foreach (var i in installs)
        {
            if (!tenantSoftwareByProductId.TryGetValue(i.SoftwareProductId, out var tenantSoftwareId))
                continue;

            var sourceSystem = ResolveSourceSystem(i.SourceKey);
            if (existingByAssetId.TryGetValue(i.Id, out var row))
            {
                var firstSeen = row.FirstSeenAt < i.FirstSeenAt ? row.FirstSeenAt : i.FirstSeenAt;
                var lastSeen = row.LastSeenAt > i.LastSeenAt ? row.LastSeenAt : i.LastSeenAt;
                row.UpdateProjection(
                    snapshotId, tenantSoftwareId, sourceSystem, i.Version,
                    firstSeen, lastSeen,
                    removedAt: null,
                    isActive: true,
                    currentEpisodeNumber: Math.Max(1, row.CurrentEpisodeNumber));
            }
            else
            {
                var fresh = SoftwareProductInstallation.Create(
                    tenantId, snapshotId, tenantSoftwareId,
                    softwareAssetId: i.Id,
                    deviceAssetId: i.DeviceId,
                    sourceSystem,
                    i.Version,
                    i.FirstSeenAt,
                    i.LastSeenAt,
                    removedAt: null,
                    isActive: true,
                    currentEpisodeNumber: 1);
                await db.SoftwareProductInstallations.AddAsync(fresh, ct);
            }
        }

        foreach (var stale in existing.Where(p => !activeInstallIds.Contains(p.SoftwareAssetId)))
        {
            stale.UpdateProjection(
                snapshotId,
                stale.TenantSoftwareId,
                stale.SourceSystem,
                stale.DetectedVersion,
                stale.FirstSeenAt,
                stale.LastSeenAt,
                removedAt: DateTimeOffset.UtcNow,
                isActive: false,
                currentEpisodeNumber: stale.CurrentEpisodeNumber);
        }

        await db.SaveChangesAsync(ct);
    }

    private static SoftwareIdentitySourceSystem ResolveSourceSystem(string? sourceKey)
    {
        return string.Equals(sourceKey, "authenticated-scan", StringComparison.OrdinalIgnoreCase)
            ? SoftwareIdentitySourceSystem.AuthenticatedScan
            : SoftwareIdentitySourceSystem.Defender;
    }
}
