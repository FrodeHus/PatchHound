using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class EnrichmentRun
{
    public Guid Id { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public EnrichmentRunStatus Status { get; private set; }
    public int JobsClaimed { get; private set; }
    public int JobsSucceeded { get; private set; }
    public int JobsNoData { get; private set; }
    public int JobsFailed { get; private set; }
    public int JobsRetried { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    private EnrichmentRun() { }

    public static EnrichmentRun Start(string sourceKey, DateTimeOffset startedAt)
    {
        return new EnrichmentRun
        {
            Id = Guid.NewGuid(),
            SourceKey = sourceKey.Trim().ToLowerInvariant(),
            StartedAt = startedAt,
            Status = EnrichmentRunStatus.Running,
        };
    }

    public void Complete(
        EnrichmentRunStatus status,
        int jobsClaimed,
        int jobsSucceeded,
        int jobsNoData,
        int jobsFailed,
        int jobsRetried,
        DateTimeOffset completedAt,
        string? lastError = null
    )
    {
        Status = status;
        JobsClaimed = jobsClaimed;
        JobsSucceeded = jobsSucceeded;
        JobsNoData = jobsNoData;
        JobsFailed = jobsFailed;
        JobsRetried = jobsRetried;
        CompletedAt = completedAt;
        LastError = lastError ?? string.Empty;
    }
}
