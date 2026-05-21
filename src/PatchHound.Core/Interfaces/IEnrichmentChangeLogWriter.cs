using PatchHound.Core.Enums;

namespace PatchHound.Core.Interfaces;

public interface IEnrichmentChangeLogWriter
{
    Task WriteScalarChangesAsync(
        EnrichmentChangeSet changeSet,
        IReadOnlyCollection<EnrichmentScalarChange> changes,
        CancellationToken ct,
        bool saveChanges = true);
}

public sealed record EnrichmentChangeSet(
    EnrichmentChangeScope Scope,
    Guid? TenantId,
    string EntityType,
    Guid EntityId,
    string SourceKey,
    Guid? EnrichmentRunId,
    Guid? EnrichmentJobId,
    DateTimeOffset ChangedAt);

public sealed record EnrichmentScalarChange(
    string FieldPath,
    string DisplayName,
    object? OldValue,
    object? NewValue,
    EnrichmentChangeValueKind ValueKind,
    string? ChangeReason = null,
    decimal? Confidence = null)
{
    public static EnrichmentScalarChange Number(string fieldPath, string displayName, decimal? oldValue, decimal? newValue) =>
        new(fieldPath, displayName, oldValue, newValue, EnrichmentChangeValueKind.Number);

    public static EnrichmentScalarChange String(string fieldPath, string displayName, string? oldValue, string? newValue) =>
        new(fieldPath, displayName, oldValue, newValue, EnrichmentChangeValueKind.String);

    public static EnrichmentScalarChange Enum<T>(string fieldPath, string displayName, T oldValue, T newValue)
        where T : struct, System.Enum =>
        new(fieldPath, displayName, oldValue.ToString(), newValue.ToString(), EnrichmentChangeValueKind.Enum);

    public static EnrichmentScalarChange DateTime(string fieldPath, string displayName, DateTimeOffset? oldValue, DateTimeOffset? newValue) =>
        new(fieldPath, displayName, oldValue, newValue, EnrichmentChangeValueKind.DateTime);
}
