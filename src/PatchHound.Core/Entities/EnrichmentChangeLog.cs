using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class EnrichmentChangeLog
{
    public const int EntityTypeMaxLength = 128;
    public const int ScopeMaxLength = 32;
    public const int SourceKeyMaxLength = 128;
    public const int FieldPathMaxLength = 256;
    public const int DisplayNameMaxLength = 256;
    public const int ValueKindMaxLength = 32;
    public const int ChangeReasonMaxLength = 1024;

    public Guid Id { get; private set; }
    public EnrichmentChangeScope Scope { get; private set; }
    public Guid? TenantId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public Guid? EnrichmentRunId { get; private set; }
    public Guid? EnrichmentJobId { get; private set; }
    public string FieldPath { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? OldValueJson { get; private set; }
    public string? NewValueJson { get; private set; }
    public EnrichmentChangeValueKind ValueKind { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string? ChangeReason { get; private set; }
    public decimal? Confidence { get; private set; }

    private EnrichmentChangeLog() { }

    public static EnrichmentChangeLog Create(
        EnrichmentChangeScope scope,
        Guid? tenantId,
        string entityType,
        Guid entityId,
        string sourceKey,
        Guid? enrichmentRunId,
        Guid? enrichmentJobId,
        string fieldPath,
        string displayName,
        string? oldValueJson,
        string? newValueJson,
        EnrichmentChangeValueKind valueKind,
        DateTimeOffset changedAt,
        string? changeReason = null,
        decimal? confidence = null
    )
    {
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported change scope.");
        }

        if (!Enum.IsDefined(valueKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(valueKind),
                valueKind,
                "Unsupported change value kind."
            );
        }

        if (scope == EnrichmentChangeScope.Global && tenantId is not null)
        {
            throw new ArgumentException("TenantId must be null for global changes.", nameof(tenantId));
        }

        if (scope == EnrichmentChangeScope.Tenant)
        {
            if (tenantId is null)
            {
                throw new ArgumentException("TenantId is required for tenant changes.", nameof(tenantId));
            }

            if (tenantId == Guid.Empty)
            {
                throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
            }
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("EntityId is required.", nameof(entityId));
        }

        if (enrichmentRunId == Guid.Empty)
        {
            throw new ArgumentException("EnrichmentRunId cannot be empty.", nameof(enrichmentRunId));
        }

        if (enrichmentJobId == Guid.Empty)
        {
            throw new ArgumentException("EnrichmentJobId cannot be empty.", nameof(enrichmentJobId));
        }

        if (changedAt == default)
        {
            throw new ArgumentException("ChangedAt is required.", nameof(changedAt));
        }

        var normalizedEntityType = NormalizeRequired(entityType, nameof(entityType));
        var normalizedSourceKey = NormalizeRequired(sourceKey, nameof(sourceKey)).ToLowerInvariant();
        var normalizedFieldPath = NormalizeRequired(fieldPath, nameof(fieldPath));
        var normalizedDisplayName = NormalizeRequired(displayName, nameof(displayName));
        var normalizedChangeReason = NormalizeOptional(changeReason);

        EnsureMaxLength(normalizedEntityType, EntityTypeMaxLength, nameof(entityType));
        EnsureMaxLength(normalizedSourceKey, SourceKeyMaxLength, nameof(sourceKey));
        EnsureMaxLength(normalizedFieldPath, FieldPathMaxLength, nameof(fieldPath));
        EnsureMaxLength(normalizedDisplayName, DisplayNameMaxLength, nameof(displayName));

        if (normalizedChangeReason is not null)
        {
            EnsureMaxLength(normalizedChangeReason, ChangeReasonMaxLength, nameof(changeReason));
        }

        return new EnrichmentChangeLog
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            TenantId = tenantId,
            EntityType = normalizedEntityType,
            EntityId = entityId,
            SourceKey = normalizedSourceKey,
            EnrichmentRunId = enrichmentRunId,
            EnrichmentJobId = enrichmentJobId,
            FieldPath = normalizedFieldPath,
            DisplayName = normalizedDisplayName,
            OldValueJson = oldValueJson,
            NewValueJson = newValueJson,
            ValueKind = valueKind,
            ChangedAt = changedAt,
            ChangeReason = normalizedChangeReason,
            Confidence = confidence,
        };
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static void EnsureMaxLength(string value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException(
                $"{parameterName} must be {maxLength} characters or fewer.",
                parameterName
            );
        }
    }
}
