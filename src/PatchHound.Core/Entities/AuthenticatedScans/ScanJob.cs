namespace PatchHound.Core.Entities.AuthenticatedScans;

public static class ScanJobStatuses
{
    public const string Pending = "Pending";
    public const string Dispatched = "Dispatched";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string TimedOut = "TimedOut";
}

public class ScanJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ScanRunnerId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid ConnectionProfileId { get; private set; }
    public string ScanningToolVersionIdsJson { get; private set; } = "[]";
    public string Status { get; private set; } = ScanJobStatuses.Pending;
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public int StdoutBytes { get; private set; }
    public int StderrBytes { get; private set; }
    public int EntriesIngested { get; private set; }

    private ScanJob() { }

    public static ScanJob Create(
        Guid tenantId, Guid runId, Guid scanRunnerId, Guid assetId,
        Guid connectionProfileId, string scanningToolVersionIdsJson)
    {
        return new ScanJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RunId = runId,
            ScanRunnerId = scanRunnerId,
            AssetId = assetId,
            ConnectionProfileId = connectionProfileId,
            ScanningToolVersionIdsJson = scanningToolVersionIdsJson,
            Status = ScanJobStatuses.Pending,
        };
    }

    public void Dispatch(DateTimeOffset leaseExpiresAt)
    {
        Status = ScanJobStatuses.Dispatched;
        LeaseExpiresAt = leaseExpiresAt;
        AttemptCount++;
    }

    public void ReturnToPending(string reason)
    {
        Status = ScanJobStatuses.Pending;
        LeaseExpiresAt = null;
        ErrorMessage = reason;
    }

    public void CompleteSucceeded(int stdoutBytes, int stderrBytes, int entriesIngested, DateTimeOffset at)
    {
        Status = ScanJobStatuses.Succeeded;
        StdoutBytes = stdoutBytes;
        StderrBytes = stderrBytes;
        EntriesIngested = entriesIngested;
        CompletedAt = at;
    }

    public void CompleteFailed(string status, string errorMessage, DateTimeOffset at)
    {
        Status = status;
        ErrorMessage = errorMessage;
        CompletedAt = at;
    }
}
