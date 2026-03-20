using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class StartNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "Start";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    )
    {
        execution.Start(instance.ContextJson);
        execution.Complete(instance.ContextJson);
        return Task.FromResult(new NodeExecutionResult(NodeExecutionOutcome.Completed));
    }
}
