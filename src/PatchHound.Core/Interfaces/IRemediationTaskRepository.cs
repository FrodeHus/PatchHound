using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface IRemediationTaskRepository : IRepository<RemediationTask>
{
    Task<IReadOnlyList<RemediationTask>> GetByAssigneeAsync(
        Guid assigneeId,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<RemediationTask>> GetByVulnerabilityAsync(
        Guid vulnerabilityId,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<RemediationTask>> GetOverdueAsync(
        Guid tenantId,
        CancellationToken ct = default
    );
}
