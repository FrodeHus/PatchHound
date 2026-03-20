using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class WorkflowInstance
{
    public Guid Id { get; private set; }
    public Guid WorkflowDefinitionId { get; private set; }
    public int DefinitionVersion { get; private set; }
    public Guid? TenantId { get; private set; }
    public WorkflowTrigger TriggerType { get; private set; }
    public string ContextJson { get; private set; } = "{}";
    public WorkflowInstanceStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Error { get; private set; }
    public Guid? CreatedBy { get; private set; }

    public WorkflowDefinition WorkflowDefinition { get; private set; } = null!;

    private readonly List<WorkflowNodeExecution> _nodeExecutions = [];
    public IReadOnlyCollection<WorkflowNodeExecution> NodeExecutions => _nodeExecutions.AsReadOnly();

    private WorkflowInstance() { }

    public static WorkflowInstance Create(
        WorkflowDefinition definition,
        string contextJson,
        Guid? createdBy
    )
    {
        return new WorkflowInstance
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            TenantId = definition.TenantId,
            TriggerType = definition.TriggerType,
            ContextJson = contextJson,
            Status = WorkflowInstanceStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
        };
    }

    public WorkflowNodeExecution AddNodeExecution(string nodeId, string nodeType)
    {
        var execution = WorkflowNodeExecution.Create(Id, nodeId, nodeType);
        _nodeExecutions.Add(execution);
        return execution;
    }

    public void UpdateContext(string contextJson)
    {
        ContextJson = contextJson;
    }

    public void MarkWaitingForAction()
    {
        Status = WorkflowInstanceStatus.WaitingForAction;
    }

    public void MarkRunning()
    {
        Status = WorkflowInstanceStatus.Running;
    }

    public void Complete()
    {
        Status = WorkflowInstanceStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string error)
    {
        Status = WorkflowInstanceStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        Status = WorkflowInstanceStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
