namespace PatchHound.Core.Enums;

public enum EnrichmentJobStatus
{
    Pending,
    Running,
    Succeeded,
    SucceededNoData,
    Failed,
    RetryScheduled,
    Skipped,
}
