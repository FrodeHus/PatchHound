using PatchHound.Core.Enums;

namespace PatchHound.Api.Models.Audit;

public record AuditLogDto(
    Guid Id,
    Guid TenantId,
    string EntityType,
    Guid EntityId,
    string? EntityLabel,
    AuditAction Action,
    string? OldValues,
    string? NewValues,
    Guid UserId,
    string? UserDisplayName,
    DateTimeOffset Timestamp
);

public record AuditLogFilterQuery(
    string? EntityType = null,
    Guid? EntityId = null,
    AuditAction? Action = null,
    Guid? UserId = null,
    Guid? TenantId = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null
);
