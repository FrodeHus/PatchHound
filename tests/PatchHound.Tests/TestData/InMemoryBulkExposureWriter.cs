using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.TestData;

/// <summary>
/// Test/InMemory implementation of <see cref="IBulkExposureWriter"/>. The
/// production implementation uses Postgres COPY + INSERT ... ON CONFLICT, which
/// the EF InMemory provider cannot honor. This fake mirrors the same upsert
/// semantics using load-mutate-SaveChanges so InMemory-backed tests can verify
/// the resulting DeviceVulnerabilityExposure rows.
/// </summary>
internal sealed class InMemoryBulkExposureWriter(PatchHoundDbContext db) : IBulkExposureWriter
{
    public async Task<BulkExposureUpsertResult> UpsertAsync(
        IReadOnlyCollection<ExposureUpsertRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0) return new BulkExposureUpsertResult(0, 0);

        var inserted = 0;
        var reobserved = 0;

        foreach (var row in rows)
        {
            var existing = await db.DeviceVulnerabilityExposures
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    e => e.TenantId == row.TenantId
                      && e.DeviceId == row.DeviceId
                      && e.VulnerabilityId == row.VulnerabilityId,
                    ct);

            if (existing is null)
            {
                var match = row.MatchSource == nameof(ExposureMatchSource.Cpe)
                    ? ExposureMatchSource.Cpe
                    : ExposureMatchSource.Product;
                var fresh = DeviceVulnerabilityExposure.Observe(
                    row.TenantId,
                    row.DeviceId,
                    row.VulnerabilityId,
                    softwareProductId: row.SoftwareProductId,
                    installedSoftwareId: row.InstalledSoftwareId,
                    matchedVersion: row.MatchedVersion ?? string.Empty,
                    match,
                    row.ObservedAt,
                    row.RunId);
                db.DeviceVulnerabilityExposures.Add(fresh);
                inserted++;
            }
            else
            {
                // The production Postgres UPSERT keeps Status=Resolved when the
                // existing row is already resolved. This fake intentionally
                // simplifies to Reobserve (which reopens) because no current
                // InMemory-backed test depends on that semantic nuance.
                existing.Reobserve(row.ObservedAt, row.RunId);
                reobserved++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new BulkExposureUpsertResult(inserted, reobserved);
    }

    public async Task<int> ResolveStaleAsync(Guid tenantId, Guid runId, DateTimeOffset resolvedAt, CancellationToken ct)
    {
        var stale = await db.DeviceVulnerabilityExposures
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId
                     && e.Status == ExposureStatus.Open
                     && e.LastSeenRunId != runId)
            .ToListAsync(ct);

        foreach (var exp in stale) exp.Resolve(resolvedAt);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
