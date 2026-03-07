using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Repositories;

public class TenantRepository : RepositoryBase<Tenant>, ITenantRepository
{
    public TenantRepository(PatchHoundDbContext dbContext)
        : base(dbContext) { }
}
