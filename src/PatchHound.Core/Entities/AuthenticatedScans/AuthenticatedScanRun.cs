namespace PatchHound.Core.Entities.AuthenticatedScans;

public static class AuthenticatedScanRunStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string PartiallyFailed = "PartiallyFailed";
    public const string Failed = "Failed";
}

public class AuthenticatedScanRun
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ScanProfileId { get; private set; }
    public string TriggerKind { get; private set; } = "scheduled";
    public Guid? TriggeredByUserId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string Status { get; private set; } = AuthenticatedScanRunStatuses.Queued;
    public int TotalDevices { get; private set; }
    public int SucceededCount { get; private set; }
    public int FailedCount { get; private set; }
    public int EntriesIngested { get; private set; }

    private AuthenticatedScanRun() { }

    public static AuthenticatedScanRun Start(
        Guid tenantId, Guid scanProfileId, string triggerKind, Guid? triggeredByUserId, DateTimeOffset at)
    {
        return new AuthenticatedScanRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScanProfileId = scanProfileId,
            TriggerKind = triggerKind,
            TriggeredByUserId = triggeredByUserId,
            StartedAt = at,
            Status = AuthenticatedScanRunStatuses.Queued,
        };
    }

    public void MarkRunning(int totalDevices)
    {
        TotalDevices = totalDevices;
        Status = AuthenticatedScanRunStatuses.Running;
    }

    public void Complete(int succeeded, int failed, int entriesIngested, DateTimeOffset at)
    {
        SucceededCount = succeeded;
        FailedCount = failed;
        EntriesIngested = entriesIngested;
        CompletedAt = at;
        Status = (failed, succeeded) switch
        {
            (0, _) => AuthenticatedScanRunStatuses.Succeeded,
            (_, 0) => AuthenticatedScanRunStatuses.Failed,
            _      => AuthenticatedScanRunStatuses.PartiallyFailed,
        };
    }
}
