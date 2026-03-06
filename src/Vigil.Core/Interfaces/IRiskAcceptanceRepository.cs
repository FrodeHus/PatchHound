using Vigil.Core.Entities;

namespace Vigil.Core.Interfaces;

public interface IRiskAcceptanceRepository : IRepository<RiskAcceptance>
{
    Task<IReadOnlyList<RiskAcceptance>> GetPendingByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<RiskAcceptance>> GetExpiredAsync(CancellationToken ct = default);
}
