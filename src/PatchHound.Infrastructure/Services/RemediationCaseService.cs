using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationCaseService(PatchHoundDbContext db)
{
    public async Task<RemediationCase> GetOrCreateAsync(
        Guid tenantId,
        Guid softwareProductId,
        CancellationToken ct)
    {
        var existing = await db.RemediationCases
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId && c.SoftwareProductId == softwareProductId,
                ct);
        if (existing is not null)
            return existing;

        var productExists = await db.SoftwareProducts
            .AsNoTracking()
            .AnyAsync(p => p.Id == softwareProductId, ct);
        if (!productExists)
            throw new InvalidOperationException(
                $"SoftwareProduct {softwareProductId} does not exist; cannot create remediation case.");

        var created = RemediationCase.Create(tenantId, softwareProductId);
        db.RemediationCases.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<RemediationCase?> GetAsync(Guid caseId, CancellationToken ct)
    {
        return await db.RemediationCases.FirstOrDefaultAsync(c => c.Id == caseId, ct);
    }

    /// <summary>
    /// Ensures a <see cref="RemediationCase"/> exists for every software product in the
    /// tenant that currently has at least one open <see cref="DeviceVulnerabilityExposure"/>.
    /// Missing cases are inserted; existing cases are untouched. Idempotent.
    /// </summary>
    public async Task<int> EnsureCasesForOpenExposuresAsync(Guid tenantId, CancellationToken ct)
    {
        var productIdsWithOpenExposures = await db.DeviceVulnerabilityExposures
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && e.Status == ExposureStatus.Open
                && e.SoftwareProductId != null)
            .Select(e => e.SoftwareProductId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (productIdsWithOpenExposures.Count == 0)
            return 0;

        var existingProductIds = await db.RemediationCases
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId
                && productIdsWithOpenExposures.Contains(c.SoftwareProductId))
            .Select(c => c.SoftwareProductId)
            .ToListAsync(ct);
        var existing = existingProductIds.ToHashSet();

        var inserted = 0;
        foreach (var productId in productIdsWithOpenExposures)
        {
            if (existing.Contains(productId))
                continue;

            db.RemediationCases.Add(RemediationCase.Create(tenantId, productId));
            inserted++;
        }

        if (inserted > 0)
            await db.SaveChangesAsync(ct);

        return inserted;
    }
}
