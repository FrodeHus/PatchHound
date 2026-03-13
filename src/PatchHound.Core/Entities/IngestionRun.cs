namespace PatchHound.Core.Entities;

public static class IngestionRunStatuses
{
    public const string Staging = "Staging";
    public const string MergePending = "MergePending";
    public const string Merging = "Merging";
    public const string Succeeded = "Succeeded";
    public const string FailedRecoverable = "FailedRecoverable";
    public const string FailedTerminal = "FailedTerminal";
}

public class IngestionRun
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? AbortRequestedAt { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public int StagedMachineCount { get; private set; }
    public int StagedSoftwareCount { get; private set; }
    public int StagedVulnerabilityCount { get; private set; }
    public int PersistedMachineCount { get; private set; }
    public int PersistedSoftwareCount { get; private set; }
    public int PersistedVulnerabilityCount { get; private set; }
    public string Error { get; private set; } = string.Empty;

    private IngestionRun() { }

    public static IngestionRun Start(Guid tenantId, string sourceKey, DateTimeOffset startedAt)
    {
        return new IngestionRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceKey = sourceKey,
            StartedAt = startedAt,
            Status = IngestionRunStatuses.Staging,
        };
    }

    public void UpdateStatus(string status)
    {
        Status = status;
    }

    public void RequestAbort(DateTimeOffset requestedAt)
    {
        if (CompletedAt.HasValue)
        {
            return;
        }

        AbortRequestedAt = requestedAt;
    }

    public void Abort(DateTimeOffset completedAt, string error)
    {
        if (CompletedAt.HasValue)
        {
            return;
        }

        CompletedAt = completedAt;
        Status = IngestionRunStatuses.FailedTerminal;
        Error = error;
    }

    public void UpdateVulnerabilityMergeProgress(
        int stagedVulnerabilityCount,
        int persistedVulnerabilityCount
    )
    {
        if (CompletedAt.HasValue)
        {
            return;
        }

        StagedVulnerabilityCount = stagedVulnerabilityCount;
        PersistedVulnerabilityCount = persistedVulnerabilityCount;
    }

    public void CompleteSucceeded(
        DateTimeOffset completedAt,
        int stagedMachineCount,
        int stagedSoftwareCount,
        int stagedVulnerabilityCount,
        int persistedMachineCount,
        int persistedSoftwareCount,
        int persistedVulnerabilityCount
    )
    {
        CompletedAt = completedAt;
        Status = IngestionRunStatuses.Succeeded;
        StagedMachineCount = stagedMachineCount;
        StagedSoftwareCount = stagedSoftwareCount;
        StagedVulnerabilityCount = stagedVulnerabilityCount;
        PersistedMachineCount = persistedMachineCount;
        PersistedSoftwareCount = persistedSoftwareCount;
        PersistedVulnerabilityCount = persistedVulnerabilityCount;
        Error = string.Empty;
    }

    public void CompleteFailed(
        DateTimeOffset completedAt,
        string error,
        string failureStatus,
        int stagedMachineCount,
        int stagedSoftwareCount,
        int stagedVulnerabilityCount,
        int persistedMachineCount,
        int persistedSoftwareCount,
        int persistedVulnerabilityCount
    )
    {
        CompletedAt = completedAt;
        Status = failureStatus;
        StagedMachineCount = stagedMachineCount;
        StagedSoftwareCount = stagedSoftwareCount;
        StagedVulnerabilityCount = stagedVulnerabilityCount;
        PersistedMachineCount = persistedMachineCount;
        PersistedSoftwareCount = persistedSoftwareCount;
        PersistedVulnerabilityCount = persistedVulnerabilityCount;
        Error = error;
    }
}
