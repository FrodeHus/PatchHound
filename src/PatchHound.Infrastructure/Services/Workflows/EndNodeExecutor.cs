using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class EndNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "End";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    )
    {
        execution.Start();
        execution.Complete();
        return Task.FromResult(new NodeExecutionResult(NodeExecutionOutcome.Completed));
    }
}
