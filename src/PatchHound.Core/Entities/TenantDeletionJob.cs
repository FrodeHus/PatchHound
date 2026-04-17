using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class TenantDeletionJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public TenantDeletionJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Error { get; private set; }

    private TenantDeletionJob() { }

    public static TenantDeletionJob Create(Guid tenantId, Guid requestedByUserId)
    {
        return new TenantDeletionJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestedByUserId = requestedByUserId,
            Status = TenantDeletionJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void MarkRunning()
    {
        Status = TenantDeletionJobStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCompleted()
    {
        Status = TenantDeletionJobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = TenantDeletionJobStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Reset(Guid requestedByUserId)
    {
        RequestedByUserId = requestedByUserId;
        Status = TenantDeletionJobStatus.Pending;
        StartedAt = null;
        CompletedAt = null;
        Error = null;
    }
}
