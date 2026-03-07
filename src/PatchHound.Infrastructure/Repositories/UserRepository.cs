using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Repositories;

public class UserRepository : RepositoryBase<User>, IUserRepository
{
    public UserRepository(PatchHoundDbContext dbContext)
        : base(dbContext) { }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        return await DbContext.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<User?> GetByEntraObjectIdAsync(
        string entraObjectId,
        CancellationToken ct = default
    )
    {
        return await DbContext.Users.FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId, ct);
    }

    public async Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken ct = default)
    {
        return await DbContext
            .Users.Include(u => u.TenantRoles)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<IReadOnlyList<User>> GetAllWithRolesAsync(CancellationToken ct = default)
    {
        return await DbContext.Users.Include(u => u.TenantRoles).ToListAsync(ct);
    }

    public async Task AddRoleAsync(UserTenantRole role, CancellationToken ct = default)
    {
        await DbContext.UserTenantRoles.AddAsync(role, ct);
    }

    public async Task<UserTenantRole?> GetRoleAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        return await DbContext.UserTenantRoles.FirstOrDefaultAsync(
            r => r.UserId == userId && r.TenantId == tenantId,
            ct
        );
    }

    public void RemoveRole(UserTenantRole role)
    {
        DbContext.UserTenantRoles.Remove(role);
    }
}
