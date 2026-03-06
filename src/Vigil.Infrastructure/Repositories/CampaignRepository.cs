using Microsoft.EntityFrameworkCore;
using Vigil.Core.Entities;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Infrastructure.Repositories;

public class CampaignRepository : RepositoryBase<Campaign>, ICampaignRepository
{
    public CampaignRepository(VigilDbContext dbContext) : base(dbContext) { }

    public async Task<IReadOnlyList<Campaign>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await DbSet.AsNoTracking().Where(c => c.TenantId == tenantId).ToListAsync(ct);
}
