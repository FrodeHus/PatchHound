using System.Text.Json;
using PatchHound.Api.Models.ApprovalTasks;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Api.Services;

internal static class AuditTimelineMapper
{
    public static ApprovalAuditEntryDto ToDto(
        AuditLogEntry entry,
        string? userDisplayName
    )
    {
        var oldValues = ParseValues(entry.OldValues);
        var newValues = ParseValues(entry.NewValues);

        var action = entry.Action switch
        {
            AuditAction.Created => "Created",
            AuditAction.Deleted => "Deleted",
            AuditAction.Denied => "Denied",
            AuditAction.Approved => "Approved",
            AuditAction.AutoDenied => "AutoDenied",
            AuditAction.Updated => ResolveUpdatedAction(oldValues, newValues),
            _ => entry.Action.ToString(),
        };

        return new ApprovalAuditEntryDto(
            action,
            userDisplayName,
            ExtractJustification(newValues),
            entry.Timestamp
        );
    }

    private static string ResolveUpdatedAction(
        IReadOnlyDictionary<string, string?> oldValues,
        IReadOnlyDictionary<string, string?> newValues
    )
    {
        if (TryGetChangedValue(oldValues, newValues, "ApprovalStatus", out _, out var newApprovalStatus))
        {
            return newApprovalStatus switch
            {
                nameof(DecisionApprovalStatus.Approved) => "Approved",
                nameof(DecisionApprovalStatus.Rejected) => "Denied",
                nameof(DecisionApprovalStatus.Expired) => "Expired",
                _ => "Updated",
            };
        }

        if (TryGetChangedValue(oldValues, newValues, "Status", out _, out var newStatus))
        {
            return newStatus switch
            {
                nameof(ApprovalTaskStatus.Approved) => "Approved",
                nameof(ApprovalTaskStatus.Denied) => "Denied",
                nameof(ApprovalTaskStatus.AutoDenied) => "AutoDenied",
                _ => "Updated",
            };
        }

        if (TryGetChangedValue(oldValues, newValues, "ReadAt", out _, out _))
        {
            return "Read";
        }

        return "Updated";
    }

    private static bool TryGetChangedValue(
        IReadOnlyDictionary<string, string?> oldValues,
        IReadOnlyDictionary<string, string?> newValues,
        string key,
        out string? oldValue,
        out string? newValue
    )
    {
        oldValues.TryGetValue(key, out oldValue);
        newValues.TryGetValue(key, out newValue);
        return !string.Equals(oldValue, newValue, StringComparison.Ordinal);
    }

    private static string? ExtractJustification(IReadOnlyDictionary<string, string?> values)
    {
        values.TryGetValue("Justification", out var justification);
        if (!string.IsNullOrWhiteSpace(justification))
        {
            return justification;
        }

        values.TryGetValue("ResolutionJustification", out justification);
        return string.IsNullOrWhiteSpace(justification) ? null : justification;
    }

    private static IReadOnlyDictionary<string, string?> ParseValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => property.Value.GetString(),
                    _ => property.Value.ToString(),
                };
            }

            return values;
        }
        catch
        {
            return Empty;
        }
    }

    private static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>(StringComparer.Ordinal);
}
