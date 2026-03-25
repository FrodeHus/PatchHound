using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RemediationWorkflowStageRecord
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationWorkflowId { get; private set; }
    public RemediationWorkflowStage Stage { get; private set; }
    public RemediationWorkflowStageStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public RoleName? AssignedRole { get; private set; }
    public Guid? AssignedTeamId { get; private set; }
    public bool SystemCompleted { get; private set; }
    public string? Summary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public RemediationWorkflow Workflow { get; private set; } = null!;

    private RemediationWorkflowStageRecord() { }

    public static RemediationWorkflowStageRecord Create(
        Guid tenantId,
        Guid remediationWorkflowId,
        RemediationWorkflowStage stage,
        RemediationWorkflowStageStatus status,
        RoleName? assignedRole = null,
        Guid? assignedTeamId = null,
        string? summary = null
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new RemediationWorkflowStageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationWorkflowId = remediationWorkflowId,
            Stage = stage,
            Status = status,
            StartedAt = now,
            AssignedRole = assignedRole,
            AssignedTeamId = assignedTeamId,
            Summary = summary,
            CreatedAt = now,
        };
    }

    public void Complete(Guid? completedByUserId, bool systemCompleted = false, string? summary = null)
    {
        Status = systemCompleted
            ? RemediationWorkflowStageStatus.AutoCompleted
            : RemediationWorkflowStageStatus.Completed;
        CompletedByUserId = completedByUserId;
        CompletedAt = DateTimeOffset.UtcNow;
        SystemCompleted = systemCompleted;
        Summary = summary ?? Summary;
    }

    public void Skip(string? summary = null)
    {
        Status = RemediationWorkflowStageStatus.Skipped;
        CompletedAt = DateTimeOffset.UtcNow;
        Summary = summary ?? Summary;
    }
}
