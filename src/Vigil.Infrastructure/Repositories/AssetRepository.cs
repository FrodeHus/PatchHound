using Microsoft.EntityFrameworkCore;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Infrastructure.Repositories;

public class AssetRepository : RepositoryBase<Asset>, IAssetRepository
{
    public AssetRepository(VigilDbContext dbContext) : base(dbContext) { }

    public async Task<Asset?> GetByExternalIdAsync(string externalId, Guid tenantId, CancellationToken ct = default)
        => await DbSet.FirstOrDefaultAsync(a => a.ExternalId == externalId && a.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<Asset>> GetByOwnerAsync(Guid ownerId, OwnerType ownerType, CancellationToken ct = default)
    {
        var query = ownerType switch
        {
            OwnerType.User => DbSet.AsNoTracking().Where(a => a.OwnerUserId == ownerId && a.OwnerType == ownerType),
            OwnerType.Team => DbSet.AsNoTracking().Where(a => a.OwnerTeamId == ownerId && a.OwnerType == ownerType),
            _ => DbSet.AsNoTracking().Where(a => false)
        };

        return await query.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Asset>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await DbSet.AsNoTracking().Where(a => a.TenantId == tenantId).ToListAsync(ct);
}
