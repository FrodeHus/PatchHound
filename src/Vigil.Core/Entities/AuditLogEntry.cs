using Vigil.Core.Enums;

namespace Vigil.Core.Entities;

public class AuditLogEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public AuditAction Action { get; private set; }
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }

    private AuditLogEntry() { }

    public static AuditLogEntry Create(
        Guid tenantId,
        string entityType,
        Guid entityId,
        AuditAction action,
        string? oldValues,
        string? newValues,
        Guid userId
    )
    {
        return new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues,
            UserId = userId,
            Timestamp = DateTimeOffset.UtcNow,
        };
    }
}
