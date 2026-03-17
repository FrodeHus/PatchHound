using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class TenantSnapshotResolver(PatchHoundDbContext dbContext)
{
    public async Task<Guid?> ResolveActiveVulnerabilitySnapshotIdAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        return await dbContext
            .TenantSourceConfigurations.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId && item.SourceKey == TenantSourceCatalog.DefenderSourceKey
            )
            .Select(item => item.ActiveSnapshotId)
            .FirstOrDefaultAsync(ct);
    }
}
