using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Repositories;

public class RiskAcceptanceRepository : RepositoryBase<RiskAcceptance>, IRiskAcceptanceRepository
{
    public RiskAcceptanceRepository(PatchHoundDbContext dbContext)
        : base(dbContext) { }

    public async Task<IReadOnlyList<RiskAcceptance>> GetPendingByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await DbSet
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status == RiskAcceptanceStatus.Pending)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RiskAcceptance>> GetExpiredAsync(
        CancellationToken ct = default
    ) =>
        await DbSet
            .AsNoTracking()
            .Where(r =>
                r.Status == RiskAcceptanceStatus.Approved
                && r.ExpiryDate != null
                && r.ExpiryDate < DateTimeOffset.UtcNow
            )
            .ToListAsync(ct);
}
