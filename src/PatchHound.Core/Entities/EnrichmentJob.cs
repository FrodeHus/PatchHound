using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class EnrichmentJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public EnrichmentTargetModel TargetModel { get; private set; }
    public Guid TargetId { get; private set; }
    public string ExternalKey { get; private set; } = null!;
    public int Priority { get; private set; }
    public EnrichmentJobStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? LeaseOwner { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private EnrichmentJob() { }

    public static EnrichmentJob Create(
        Guid tenantId,
        string sourceKey,
        EnrichmentTargetModel targetModel,
        Guid targetId,
        string externalKey,
        int priority,
        DateTimeOffset queuedAt
    )
    {
        return new EnrichmentJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceKey = sourceKey.Trim().ToLowerInvariant(),
            TargetModel = targetModel,
            TargetId = targetId,
            ExternalKey = externalKey,
            Priority = priority,
            Status = EnrichmentJobStatus.Pending,
            Attempts = 0,
            NextAttemptAt = queuedAt,
            CreatedAt = queuedAt,
            UpdatedAt = queuedAt,
        };
    }

    public void Refresh(int priority, DateTimeOffset nextAttemptAt)
    {
        Priority = priority;
        NextAttemptAt = nextAttemptAt;
        if (Status != EnrichmentJobStatus.Running)
        {
            Status = EnrichmentJobStatus.Pending;
            LastError = string.Empty;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Start(string leaseOwner, DateTimeOffset startedAt, DateTimeOffset leaseExpiresAt)
    {
        Status = EnrichmentJobStatus.Running;
        Attempts++;
        LastStartedAt = startedAt;
        LeaseOwner = leaseOwner;
        LeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = startedAt;
    }

    public void Complete(
        EnrichmentJobStatus status,
        DateTimeOffset completedAt,
        string? error = null
    )
    {
        Status = status;
        LastCompletedAt = completedAt;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        LastError = error ?? string.Empty;
        UpdatedAt = completedAt;
    }

    public void ScheduleRetry(DateTimeOffset nextAttemptAt, DateTimeOffset updatedAt, string error)
    {
        Status = EnrichmentJobStatus.RetryScheduled;
        NextAttemptAt = nextAttemptAt;
        LastCompletedAt = updatedAt;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        LastError = error;
        UpdatedAt = updatedAt;
    }
}
