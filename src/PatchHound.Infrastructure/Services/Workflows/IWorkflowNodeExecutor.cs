using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services.Workflows;

public interface IWorkflowNodeExecutor
{
    string NodeType { get; }

    Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    );
}

public enum NodeExecutionOutcome
{
    Completed,
    WaitingForAction,
    Failed,
}

public sealed record NodeExecutionResult(
    NodeExecutionOutcome Outcome,
    string? OutputJson = null,
    string? Error = null,
    string? NextEdgeLabel = null
);
