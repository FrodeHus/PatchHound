namespace PatchHound.Core.Models;

public sealed record SentinelAuditEvent(
    Guid AuditEntryId,
    Guid TenantId,
    string EntityType,
    Guid EntityId,
    string Action,
    string? OldValues,
    string? NewValues,
    Guid UserId,
    DateTimeOffset Timestamp
);
