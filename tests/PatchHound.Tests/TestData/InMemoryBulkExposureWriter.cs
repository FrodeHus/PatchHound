using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.TestData;

/// <summary>
/// In-memory replacement for <see cref="IBulkExposureWriter"/> used by Phase-3 tests that
/// run against the EF Core InMemory provider (which cannot execute the real PostgreSQL UPSERT).
///
/// IMPORTANT: This fake DIVERGES from the production writer in one semantic:
/// on conflict it calls <c>Reobserve</c> which reopens a previously-Resolved exposure.
/// The real <see cref="PatchHound.Infrastructure.Services.PostgresBulkExposureWriter"/> preserves <c>Status='Resolved'</c>
/// on conflict (see <c>ExposureDerivationService.cs:74</c> — "respect direct-report resolution").
///
/// Do NOT use this fake in tests that seed a pre-existing Resolved exposure and then
/// verify upsert behavior on it — the fake will return the wrong status. Either use
/// the Testcontainers Postgres fixture, or substitute a per-test mock via NSubstitute.
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
