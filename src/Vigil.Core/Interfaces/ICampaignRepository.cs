using Vigil.Core.Entities;

namespace Vigil.Core.Interfaces;

public interface ICampaignRepository : IRepository<Campaign>
{
    Task<IReadOnlyList<Campaign>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
