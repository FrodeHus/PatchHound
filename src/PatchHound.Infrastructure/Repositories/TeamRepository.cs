using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Repositories;

public class TeamRepository : RepositoryBase<Team>, ITeamRepository
{
    public TeamRepository(PatchHoundDbContext dbContext)
        : base(dbContext) { }

    public async Task<Team?> GetByIdWithMembersAsync(Guid id, CancellationToken ct = default)
    {
        return await DbContext
            .Teams.Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<IReadOnlyList<Team>> GetByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        return await DbContext.Teams.Where(t => t.TenantId == tenantId).ToListAsync(ct);
    }
}
