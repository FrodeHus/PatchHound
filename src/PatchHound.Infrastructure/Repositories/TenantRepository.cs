using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Repositories;

public class TenantRepository : RepositoryBase<Tenant>, ITenantRepository
{
    public TenantRepository(PatchHoundDbContext dbContext)
        : base(dbContext) { }

    public async Task<bool> AnyExistUnfilteredAsync(CancellationToken ct = default) =>
        await DbSet.IgnoreQueryFilters().AnyAsync(ct);

    public async Task<bool> ExistsByEntraTenantIdUnfilteredAsync(
        string entraTenantId,
        CancellationToken ct = default
    ) =>
        await DbSet
            .IgnoreQueryFilters()
            .AnyAsync(tenant => tenant.EntraTenantId == entraTenantId, ct);
}
