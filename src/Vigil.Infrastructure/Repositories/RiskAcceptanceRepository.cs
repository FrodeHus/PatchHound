using Microsoft.EntityFrameworkCore;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Infrastructure.Repositories;

public class RiskAcceptanceRepository : RepositoryBase<RiskAcceptance>, IRiskAcceptanceRepository
{
    public RiskAcceptanceRepository(VigilDbContext dbContext) : base(dbContext) { }

    public async Task<IReadOnlyList<RiskAcceptance>> GetPendingByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await DbSet.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status == RiskAcceptanceStatus.Pending)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RiskAcceptance>> GetExpiredAsync(CancellationToken ct = default)
        => await DbSet.AsNoTracking()
            .Where(r => r.Status == RiskAcceptanceStatus.Approved
                        && r.ExpiryDate != null
                        && r.ExpiryDate < DateTimeOffset.UtcNow)
            .ToListAsync(ct);
}
