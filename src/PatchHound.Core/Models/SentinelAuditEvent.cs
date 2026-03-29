namespace PatchHound.Core.Models;

public sealed record SentinelAuditEvent(
    Guid AuditEntryId,
    Guid Tenant,
    string EntityType,
    Guid EntityId,
    string Action,
    string? OldValues,
    string? NewValues,
    Guid UserId,
    DateTimeOffset Timestamp
);
