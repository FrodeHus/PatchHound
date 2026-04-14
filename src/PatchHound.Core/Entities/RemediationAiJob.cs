using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RemediationAiJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public RemediationAiJobStatus Status { get; private set; }
    public string InputHash { get; private set; } = string.Empty;
    public string Error { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RemediationAiJob() { }

    public static RemediationAiJob Create(
        Guid tenantId,
        Guid remediationCaseId,
        string inputHash,
        DateTimeOffset requestedAt)
    {
        return new RemediationAiJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            Status = RemediationAiJobStatus.Pending,
            InputHash = inputHash.Trim(),
            RequestedAt = requestedAt,
            UpdatedAt = requestedAt,
        };
    }

    public void Refresh(string inputHash, DateTimeOffset requestedAt)
    {
        InputHash = inputHash.Trim();
        RequestedAt = requestedAt;
        UpdatedAt = requestedAt;
        if (Status != RemediationAiJobStatus.Running)
        {
            Status = RemediationAiJobStatus.Pending;
            Error = string.Empty;
            StartedAt = null;
            CompletedAt = null;
        }
    }

    public void Start(DateTimeOffset startedAt)
    {
        Status = RemediationAiJobStatus.Running;
        StartedAt = startedAt;
        CompletedAt = null;
        Error = string.Empty;
        UpdatedAt = startedAt;
    }

    public void CompleteSucceeded(DateTimeOffset completedAt)
    {
        Status = RemediationAiJobStatus.Succeeded;
        CompletedAt = completedAt;
        Error = string.Empty;
        UpdatedAt = completedAt;
    }

    public void CompleteFailed(DateTimeOffset completedAt, string error)
    {
        Status = RemediationAiJobStatus.Failed;
        CompletedAt = completedAt;
        Error = error.Trim();
        UpdatedAt = completedAt;
    }
}
