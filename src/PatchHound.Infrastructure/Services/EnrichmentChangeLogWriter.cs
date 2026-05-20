using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public sealed class EnrichmentChangeLogWriter(
    PatchHoundDbContext db,
    ILogger<EnrichmentChangeLogWriter> logger
) : IEnrichmentChangeLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteScalarChangesAsync(
        EnrichmentChangeSet changeSet,
        IReadOnlyCollection<EnrichmentScalarChange> changes,
        CancellationToken ct)
    {
        var addedLogs = new List<EnrichmentChangeLog>();

        foreach (var change in changes)
        {
            if (ValuesEqual(change.OldValue, change.NewValue))
            {
                continue;
            }

            var log = EnrichmentChangeLog.Create(
                changeSet.Scope,
                changeSet.TenantId,
                changeSet.EntityType,
                changeSet.EntityId,
                changeSet.SourceKey,
                changeSet.EnrichmentRunId,
                changeSet.EnrichmentJobId,
                change.FieldPath,
                change.DisplayName,
                SerializeValue(change.OldValue),
                SerializeValue(change.NewValue),
                change.ValueKind,
                changeSet.ChangedAt,
                change.ChangeReason,
                change.Confidence
            );

            db.EnrichmentChangeLogs.Add(log);
            addedLogs.Add(log);
        }

        if (addedLogs.Count == 0)
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to persist enrichment change log entries for {EntityType} {EntityId}.",
                changeSet.EntityType,
                changeSet.EntityId
            );

            foreach (var log in addedLogs)
            {
                var entry = db.Entry(log);
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
    }

    private static bool ValuesEqual(object? oldValue, object? newValue) =>
        oldValue switch
        {
            null => newValue is null,
            decimal oldDecimal when newValue is decimal newDecimal => oldDecimal == newDecimal,
            _ => oldValue.Equals(newValue),
        };

    private static string SerializeValue(object? value)
    {
        if (value is decimal decimalValue)
        {
            var normalized = decimal.Parse(
                decimalValue.ToString("G29", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture
            );

            return JsonSerializer.Serialize(normalized, JsonOptions);
        }

        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
