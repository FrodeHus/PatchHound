using Vigil.Core.Enums;

namespace Vigil.Api.Models.Audit;

public record AuditLogDto(
    Guid Id,
    Guid TenantId,
    string EntityType,
    Guid EntityId,
    AuditAction Action,
    string? OldValues,
    string? NewValues,
    Guid UserId,
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
