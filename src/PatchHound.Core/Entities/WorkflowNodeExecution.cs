using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class WorkflowNodeExecution
{
    public Guid Id { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string NodeId { get; private set; } = null!;
    public string NodeType { get; private set; } = null!;
    public WorkflowNodeExecutionStatus Status { get; private set; }
    public string? InputJson { get; private set; }
    public string? OutputJson { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? AssignedTeamId { get; private set; }
    public Guid? CompletedByUserId { get; private set; }

    public WorkflowInstance WorkflowInstance { get; private set; } = null!;

    private WorkflowNodeExecution() { }

    internal static WorkflowNodeExecution Create(Guid instanceId, string nodeId, string nodeType)
    {
        return new WorkflowNodeExecution
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instanceId,
            NodeId = nodeId,
            NodeType = nodeType,
            Status = WorkflowNodeExecutionStatus.Pending,
        };
    }

    public void Start(string? inputJson = null)
    {
        Status = WorkflowNodeExecutionStatus.Running;
        InputJson = inputJson;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void MarkWaitingForAction(Guid teamId)
    {
        Status = WorkflowNodeExecutionStatus.WaitingForAction;
        AssignedTeamId = teamId;
    }

    public void Complete(string? outputJson = null, Guid? completedByUserId = null)
    {
        Status = WorkflowNodeExecutionStatus.Completed;
        OutputJson = outputJson;
        CompletedByUserId = completedByUserId;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string error)
    {
        Status = WorkflowNodeExecutionStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Skip()
    {
        Status = WorkflowNodeExecutionStatus.Skipped;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
