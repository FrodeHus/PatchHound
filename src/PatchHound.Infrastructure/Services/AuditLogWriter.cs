using System.Text.Json;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class AuditLogWriter
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly SentinelAuditQueue? _sentinelQueue;

    public AuditLogWriter(
        PatchHoundDbContext dbContext,
        ITenantContext tenantContext,
        SentinelAuditQueue? sentinelQueue = null
    )
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _sentinelQueue = sentinelQueue;
    }

    public async Task WriteAsync(
        Guid tenantId,
        string entityType,
        Guid entityId,
        AuditAction action,
        object? oldValues,
        object? newValues,
        CancellationToken ct
    )
    {
        var entry = AuditLogEntry.Create(
            tenantId,
            entityType,
            entityId,
            action,
            SerializeValues(oldValues),
            SerializeValues(newValues),
            _tenantContext.CurrentUserId
        );

        await _dbContext.AuditLogEntries.AddAsync(entry, ct);

        _sentinelQueue?.TryWrite(new Core.Models.SentinelAuditEvent(
            AuditEntryId: entry.Id,
            TenantId: entry.TenantId,
            EntityType: entry.EntityType,
            EntityId: entry.EntityId,
            Action: entry.Action.ToString(),
            OldValues: entry.OldValues,
            NewValues: entry.NewValues,
            UserId: entry.UserId,
            Timestamp: entry.Timestamp
        ));
    }

    private static string? SerializeValues(object? values)
    {
        return values is null ? null : JsonSerializer.Serialize(values);
    }
}
