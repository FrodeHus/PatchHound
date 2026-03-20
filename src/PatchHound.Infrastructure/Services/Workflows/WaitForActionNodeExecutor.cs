using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class WaitForActionNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "WaitForAction";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    )
    {
        // WaitForAction is a pass-through. The preceding AssignGroup node
        // already created the WorkflowAction and paused the branch. By the
        // time the engine reaches this node the action has been completed.
        execution.Start(instance.ContextJson);
        execution.Complete(instance.ContextJson);
        return Task.FromResult(new NodeExecutionResult(NodeExecutionOutcome.Completed));
    }
}
