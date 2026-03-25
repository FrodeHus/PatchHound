using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RemediationWorkflow
{
    private readonly List<RemediationWorkflowStageRecord> _stageRecords = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TenantSoftwareId { get; private set; }
    public Guid SoftwareOwnerTeamId { get; private set; }
    public Guid? RecurrenceSourceWorkflowId { get; private set; }
    public RemediationWorkflowStage CurrentStage { get; private set; }
    public RemediationWorkflowStatus Status { get; private set; }
    public RemediationOutcome? ProposedOutcome { get; private set; }
    public RemediationWorkflowPriority? Priority { get; private set; }
    public RemediationWorkflowApprovalMode ApprovalMode { get; private set; }
    public DateTimeOffset CurrentStageStartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<RemediationWorkflowStageRecord> StageRecords => _stageRecords.AsReadOnly();

    private RemediationWorkflow() { }

    public static RemediationWorkflow Create(
        Guid tenantId,
        Guid tenantSoftwareId,
        Guid softwareOwnerTeamId,
        RemediationWorkflowStage initialStage = RemediationWorkflowStage.SecurityAnalysis,
        Guid? recurrenceSourceWorkflowId = null
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new RemediationWorkflow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TenantSoftwareId = tenantSoftwareId,
            SoftwareOwnerTeamId = softwareOwnerTeamId,
            RecurrenceSourceWorkflowId = recurrenceSourceWorkflowId,
            CurrentStage = initialStage,
            Status = RemediationWorkflowStatus.Active,
            ApprovalMode = RemediationWorkflowApprovalMode.None,
            CurrentStageStartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetDecisionContext(
        RemediationOutcome? proposedOutcome,
        RemediationWorkflowPriority? priority,
        RemediationWorkflowApprovalMode approvalMode
    )
    {
        ProposedOutcome = proposedOutcome;
        Priority = priority;
        ApprovalMode = approvalMode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MoveToStage(RemediationWorkflowStage stage)
    {
        CurrentStage = stage;
        CurrentStageStartedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        Status = RemediationWorkflowStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        Status = RemediationWorkflowStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
