using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Repositories;

public class AuditLogRepository : RepositoryBase<AuditLogEntry>, IAuditLogRepository
{
    public AuditLogRepository(PatchHoundDbContext dbContext)
        : base(dbContext) { }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default
    ) =>
        await DbSet
            .AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AuditLogEntry>> GetByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await DbSet
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct);
}
