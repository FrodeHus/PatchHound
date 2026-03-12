namespace PatchHound.Core.Entities;

public class TenantSourceConfiguration
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public bool Enabled { get; private set; }
    public string SyncSchedule { get; private set; } = string.Empty;
    public string CredentialTenantId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string SecretRef { get; private set; } = string.Empty;
    public string ApiBaseUrl { get; private set; } = string.Empty;
    public string TokenScope { get; private set; } = string.Empty;
    public DateTimeOffset? ManualRequestedAt { get; private set; }
    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public DateTimeOffset? LastSucceededAt { get; private set; }
    public Guid? ActiveIngestionRunId { get; private set; }
    public Guid? ActiveSnapshotId { get; private set; }
    public Guid? BuildingSnapshotId { get; private set; }
    public DateTimeOffset? LeaseAcquiredAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string LastStatus { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    private TenantSourceConfiguration() { }

    public static TenantSourceConfiguration Create(
        Guid tenantId,
        string sourceKey,
        string displayName,
        bool enabled,
        string syncSchedule,
        string credentialTenantId = "",
        string clientId = "",
        string secretRef = "",
        string apiBaseUrl = "",
        string tokenScope = ""
    )
    {
        return new TenantSourceConfiguration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceKey = sourceKey,
            DisplayName = displayName,
            Enabled = enabled,
            SyncSchedule = syncSchedule,
            CredentialTenantId = credentialTenantId,
            ClientId = clientId,
            SecretRef = secretRef,
            ApiBaseUrl = apiBaseUrl,
            TokenScope = tokenScope,
        };
    }

    public void UpdateConfiguration(
        string displayName,
        bool enabled,
        string syncSchedule,
        string credentialTenantId,
        string clientId,
        string secretRef,
        string apiBaseUrl,
        string tokenScope
    )
    {
        DisplayName = displayName;
        Enabled = enabled;
        SyncSchedule = syncSchedule;
        CredentialTenantId = credentialTenantId;
        ClientId = clientId;
        SecretRef = secretRef;
        ApiBaseUrl = apiBaseUrl;
        TokenScope = tokenScope;
    }

    public void UpdateRuntime(
        DateTimeOffset? manualRequestedAt,
        DateTimeOffset? lastStartedAt,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset? lastSucceededAt,
        string lastStatus,
        string lastError
    )
    {
        ManualRequestedAt = manualRequestedAt;
        LastStartedAt = lastStartedAt;
        LastCompletedAt = lastCompletedAt;
        LastSucceededAt = lastSucceededAt;
        LastStatus = lastStatus;
        LastError = lastError;
    }

    public void QueueManualSync(DateTimeOffset requestedAt)
    {
        ManualRequestedAt = requestedAt;
        LastStatus = "Queued";
        LastError = string.Empty;
    }

    public void MarkStarted(DateTimeOffset startedAt)
    {
        ManualRequestedAt = null;
        LastStartedAt = startedAt;
        LastStatus = "Running";
        LastError = string.Empty;
    }

    public void MarkSucceeded(DateTimeOffset completedAt)
    {
        LastCompletedAt = completedAt;
        LastSucceededAt = completedAt;
        LastStatus = "Succeeded";
        LastError = string.Empty;
    }

    public void MarkFailed(DateTimeOffset completedAt, string error)
    {
        LastCompletedAt = completedAt;
        LastStatus = "Failed";
        LastError = error;
    }

    public void AcquireLease(Guid runId, DateTimeOffset acquiredAt, DateTimeOffset expiresAt)
    {
        ActiveIngestionRunId = runId;
        LeaseAcquiredAt = acquiredAt;
        LeaseExpiresAt = expiresAt;
    }

    public void ReleaseLease(Guid runId)
    {
        if (ActiveIngestionRunId != runId)
        {
            return;
        }

        ActiveIngestionRunId = null;
        LeaseAcquiredAt = null;
        LeaseExpiresAt = null;
    }

    public void SetSnapshotPointers(Guid? activeSnapshotId, Guid? buildingSnapshotId)
    {
        ActiveSnapshotId = activeSnapshotId;
        BuildingSnapshotId = buildingSnapshotId;
    }
}
