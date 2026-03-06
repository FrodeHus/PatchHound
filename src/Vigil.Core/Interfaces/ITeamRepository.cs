using Vigil.Core.Entities;

namespace Vigil.Core.Interfaces;

public interface ITeamRepository : IRepository<Team>
{
    Task<Team?> GetByIdWithMembersAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Team>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
