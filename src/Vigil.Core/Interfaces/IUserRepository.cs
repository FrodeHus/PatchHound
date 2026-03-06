using Vigil.Core.Entities;

namespace Vigil.Core.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllWithRolesAsync(CancellationToken ct = default);
    Task AddRoleAsync(UserTenantRole role, CancellationToken ct = default);
    Task<UserTenantRole?> GetRoleAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    void RemoveRole(UserTenantRole role);
}
