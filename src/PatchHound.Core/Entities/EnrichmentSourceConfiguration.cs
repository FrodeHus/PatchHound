namespace PatchHound.Core.Entities;

public class EnrichmentSourceConfiguration
{
    public Guid Id { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public bool Enabled { get; private set; }
    public string SecretRef { get; private set; } = string.Empty;
    public string ApiBaseUrl { get; private set; } = string.Empty;
    public int? RefreshTtlHours { get; private set; }
    public Guid? ActiveEnrichmentRunId { get; private set; }
    public DateTimeOffset? LeaseAcquiredAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public DateTimeOffset? LastSucceededAt { get; private set; }
    public string LastStatus { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    private EnrichmentSourceConfiguration() { }

    public static EnrichmentSourceConfiguration Create(
        string sourceKey,
        string displayName,
        bool enabled,
        string secretRef = "",
        string apiBaseUrl = "",
        int? refreshTtlHours = null
    )
    {
        return new EnrichmentSourceConfiguration
        {
            Id = Guid.NewGuid(),
            SourceKey = sourceKey,
            DisplayName = displayName,
            Enabled = enabled,
            SecretRef = secretRef,
            ApiBaseUrl = apiBaseUrl,
            RefreshTtlHours = refreshTtlHours,
        };
    }

    public void UpdateConfiguration(
        string displayName,
        bool enabled,
        string secretRef,
        string apiBaseUrl,
        int? refreshTtlHours
    )
    {
        DisplayName = displayName;
        Enabled = enabled;
        SecretRef = secretRef;
        ApiBaseUrl = apiBaseUrl;
        RefreshTtlHours = refreshTtlHours;
    }

    public void UpdateRuntime(
        DateTimeOffset? lastStartedAt,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset? lastSucceededAt,
        string lastStatus,
        string lastError
    )
    {
        LastStartedAt = lastStartedAt;
        LastCompletedAt = lastCompletedAt;
        LastSucceededAt = lastSucceededAt;
        LastStatus = lastStatus;
        LastError = lastError;
    }

    public void AcquireLease(Guid runId, DateTimeOffset acquiredAt, DateTimeOffset expiresAt)
    {
        ActiveEnrichmentRunId = runId;
        LeaseAcquiredAt = acquiredAt;
        LeaseExpiresAt = expiresAt;
    }

    public void ReleaseLease()
    {
        ActiveEnrichmentRunId = null;
        LeaseAcquiredAt = null;
        LeaseExpiresAt = null;
    }
}
