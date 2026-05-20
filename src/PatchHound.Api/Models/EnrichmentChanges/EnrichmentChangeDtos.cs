namespace PatchHound.Api.Models.EnrichmentChanges;

public record EnrichmentChangeFilterQuery(
    string EntityType,
    Guid EntityId,
    string? SourceKey,
    string? FieldPath,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate
);

public record EnrichmentChangeDto(
    Guid Id,
    string Scope,
    Guid? TenantId,
    string EntityType,
    Guid EntityId,
    string SourceKey,
    string? SourceDisplayName,
    string FieldPath,
    string DisplayName,
    object? OldValue,
    object? NewValue,
    string ValueKind,
    DateTimeOffset ChangedAt,
    Guid? EnrichmentRunId,
    Guid? EnrichmentJobId,
    string? ChangeReason,
    decimal? Confidence
);
