using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class WorkflowAction
{
    public Guid Id { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public Guid NodeExecutionId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TeamId { get; private set; }
    public WorkflowRequiredActionType ActionType { get; private set; }
    public string? Instructions { get; private set; }
    public WorkflowActionStatus Status { get; private set; }
    public string? ResponseJson { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedByUserId { get; private set; }

    public WorkflowInstance WorkflowInstance { get; private set; } = null!;
    public WorkflowNodeExecution NodeExecution { get; private set; } = null!;

    private WorkflowAction() { }

    public static WorkflowAction Create(
        Guid instanceId,
        Guid nodeExecutionId,
        Guid tenantId,
        Guid teamId,
        WorkflowRequiredActionType actionType,
        string? instructions,
        DateTimeOffset? dueAt
    )
    {
        return new WorkflowAction
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instanceId,
            NodeExecutionId = nodeExecutionId,
            TenantId = tenantId,
            TeamId = teamId,
            ActionType = actionType,
            Instructions = instructions,
            Status = WorkflowActionStatus.Pending,
            DueAt = dueAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Complete(Guid userId, string? responseJson)
    {
        Status = WorkflowActionStatus.Completed;
        CompletedByUserId = userId;
        ResponseJson = responseJson;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(Guid userId, string? responseJson)
    {
        Status = WorkflowActionStatus.Rejected;
        CompletedByUserId = userId;
        ResponseJson = responseJson;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void TimeOut()
    {
        Status = WorkflowActionStatus.TimedOut;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
