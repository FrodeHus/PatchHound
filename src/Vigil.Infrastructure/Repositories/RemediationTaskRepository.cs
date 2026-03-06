using Microsoft.EntityFrameworkCore;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Infrastructure.Repositories;

public class RemediationTaskRepository : RepositoryBase<RemediationTask>, IRemediationTaskRepository
{
    public RemediationTaskRepository(VigilDbContext dbContext) : base(dbContext) { }

    public async Task<IReadOnlyList<RemediationTask>> GetByAssigneeAsync(Guid assigneeId, CancellationToken ct = default)
        => await DbSet.AsNoTracking().Where(t => t.AssigneeId == assigneeId).ToListAsync(ct);

    public async Task<IReadOnlyList<RemediationTask>> GetByVulnerabilityAsync(Guid vulnerabilityId, CancellationToken ct = default)
        => await DbSet.AsNoTracking().Where(t => t.VulnerabilityId == vulnerabilityId).ToListAsync(ct);

    public async Task<IReadOnlyList<RemediationTask>> GetOverdueAsync(Guid tenantId, CancellationToken ct = default)
        => await DbSet.AsNoTracking()
            .Where(t => t.TenantId == tenantId
                        && t.DueDate < DateTimeOffset.UtcNow
                        && t.Status != RemediationTaskStatus.Completed)
            .ToListAsync(ct);
}
