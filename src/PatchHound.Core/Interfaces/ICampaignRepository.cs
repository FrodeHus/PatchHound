using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface ICampaignRepository : IRepository<Campaign>
{
    Task<IReadOnlyList<Campaign>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
