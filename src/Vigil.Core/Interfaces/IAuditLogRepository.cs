using Vigil.Core.Entities;

namespace Vigil.Core.Interfaces;

public interface IAuditLogRepository : IRepository<AuditLogEntry>
{
    Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(string entityType, Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntry>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
