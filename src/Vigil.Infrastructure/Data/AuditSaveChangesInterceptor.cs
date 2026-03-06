using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;

namespace Vigil.Infrastructure.Data;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    public AuditSaveChangesInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditLogEntry)
            .ToList();

        foreach (var entry in entries)
        {
            var entityId = GetEntityId(entry);
            var tenantId = GetTenantId(entry);
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Modified => AuditAction.Updated,
                EntityState.Deleted => AuditAction.Deleted,
                _ => throw new InvalidOperationException()
            };

            string? oldValues = entry.State != EntityState.Added
                ? SerializeValues(entry.Properties
                    .Where(p => entry.State == EntityState.Deleted || p.IsModified)
                    .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue))
                : null;

            string? newValues = entry.State != EntityState.Deleted
                ? SerializeValues(entry.Properties
                    .Where(p => entry.State == EntityState.Added || p.IsModified)
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue))
                : null;

            var auditEntry = AuditLogEntry.Create(
                tenantId,
                entry.Entity.GetType().Name,
                entityId,
                action,
                oldValues,
                newValues,
                _tenantContext.CurrentUserId);

            context.Set<AuditLogEntry>().Add(auditEntry);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static Guid GetEntityId(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
        return idProperty?.CurrentValue is Guid id ? id : Guid.Empty;
    }

    private static Guid GetTenantId(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var tenantProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "TenantId");
        return tenantProperty?.CurrentValue is Guid tenantId ? tenantId : Guid.Empty;
    }

    private static string? SerializeValues(Dictionary<string, object?> values)
    {
        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }
}
