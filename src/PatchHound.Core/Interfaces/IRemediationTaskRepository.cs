using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface IRemediationTaskRepository : IRepository<RemediationTask>
{
    Task<IReadOnlyList<RemediationTask>> GetByAssigneeAsync(
        Guid assigneeId,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<RemediationTask>> GetByTenantVulnerabilityAsync(
        Guid tenantVulnerabilityId,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<RemediationTask>> GetOverdueAsync(
        Guid tenantId,
        CancellationToken ct = default
    );
}
