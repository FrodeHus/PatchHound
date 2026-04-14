using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
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
}
