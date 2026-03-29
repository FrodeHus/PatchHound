using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Infrastructure.Data;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> ExcludedProperties = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "ClientSecret",
        "PasswordHash",
    };
    private static readonly HashSet<string> NoiseOnlyModifiedProperties = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "UpdatedAt",
        "LastExecutedAt",
        "LastMatchCount",
        "LeaseAcquiredAt",
        "LeaseExpiresAt",
        "ActiveIngestionRunId",
        "ActiveEnrichmentRunId",
        "LastStartedAt",
        "LastCompletedAt",
        "LastSucceededAt",
        "LastStatus",
        "LastError",
        "ManualRequestedAt",
        "CompletedAt",
        "Error",
        "StagedMachineCount",
        "StagedSoftwareCount",
        "StagedVulnerabilityCount",
        "PersistedMachineCount",
        "PersistedSoftwareCount",
        "PersistedVulnerabilityCount",
        "DeactivatedMachineCount",
        "Status",
    };
    private static readonly HashSet<string> IngestionCleanupEntityTypes = new(
        StringComparer.Ordinal
    )
    {
        nameof(IngestionRun),
        nameof(IngestionCheckpoint),
        nameof(StagedAsset),
        nameof(StagedVulnerability),
        nameof(StagedVulnerabilityExposure),
        nameof(StagedDeviceSoftwareInstallation),
    };

    private readonly ITenantContext _tenantContext;
    private readonly SentinelAuditQueue? _sentinelQueue;

    public AuditSaveChangesInterceptor(
        ITenantContext tenantContext,
        SentinelAuditQueue? sentinelQueue = null
    )
    {
        _tenantContext = tenantContext;
        _sentinelQueue = sentinelQueue;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        var context = eventData.Context;
        if (context is null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        // Skip audit logging for system/worker operations (no authenticated user)
        if (_tenantContext.CurrentUserId == Guid.Empty)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var entries = context
            .ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditLogEntry)
            .ToList();

        foreach (var entry in entries)
        {
            if (ShouldSkipAudit(entry))
            {
                continue;
            }

            var auditableProperties = entry
                .Properties.Where(p => !ExcludedProperties.Contains(p.Metadata.Name))
                .ToList();

            var auditableChangedProperties = entry.State switch
            {
                EntityState.Modified => auditableProperties.Where(p => p.IsModified).ToList(),
                EntityState.Added => auditableProperties,
                EntityState.Deleted => auditableProperties,
                _ => [],
            };

            if (entry.State == EntityState.Modified && ShouldSkipNoiseOnlyUpdate(auditableChangedProperties))
            {
                continue;
            }

            var entityId = GetEntityId(entry);
            var tenantId = GetTenantId(entry);
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Modified => AuditAction.Updated,
                EntityState.Deleted => AuditAction.Deleted,
                _ => throw new InvalidOperationException(),
            };

            string? oldValues =
                entry.State != EntityState.Added
                    ? SerializeValues(
                        auditableChangedProperties
                            .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue)
                    )
                    : null;

            string? newValues =
                entry.State != EntityState.Deleted
                    ? SerializeValues(
                        auditableChangedProperties
                            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)
                    )
                    : null;

            var auditEntry = AuditLogEntry.Create(
                tenantId,
                entry.Entity.GetType().Name,
                entityId,
                action,
                oldValues,
                newValues,
                _tenantContext.CurrentUserId
            );

            context.Set<AuditLogEntry>().Add(auditEntry);

            _sentinelQueue?.TryWrite(new PatchHound.Core.Models.SentinelAuditEvent(
                AuditEntryId: auditEntry.Id,
                TenantId: auditEntry.TenantId,
                EntityType: auditEntry.EntityType,
                EntityId: auditEntry.EntityId,
                Action: action.ToString(),
                OldValues: oldValues,
                NewValues: newValues,
                UserId: auditEntry.UserId,
                Timestamp: auditEntry.Timestamp
            ));
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static bool ShouldSkipAudit(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        return entry.State == EntityState.Deleted
            && IngestionCleanupEntityTypes.Contains(entry.Entity.GetType().Name);
    }

    private static bool ShouldSkipNoiseOnlyUpdate(
        List<Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry> changedProperties
    )
    {
        return changedProperties.Count > 0
            && changedProperties.All(p => NoiseOnlyModifiedProperties.Contains(p.Metadata.Name));
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
