using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface ITeamRepository : IRepository<Team>
{
    Task<Team?> GetByIdWithMembersAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Team>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
