using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Repositories;

public class CampaignRepository : RepositoryBase<Campaign>, ICampaignRepository
{
    public CampaignRepository(PatchHoundDbContext dbContext)
        : base(dbContext) { }

    public async Task<IReadOnlyList<Campaign>> GetByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => await DbSet.AsNoTracking().Where(c => c.TenantId == tenantId).ToListAsync(ct);
}
