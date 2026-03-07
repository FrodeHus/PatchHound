using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface IRiskAcceptanceRepository : IRepository<RiskAcceptance>
{
    Task<IReadOnlyList<RiskAcceptance>> GetPendingByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<RiskAcceptance>> GetExpiredAsync(CancellationToken ct = default);
}
