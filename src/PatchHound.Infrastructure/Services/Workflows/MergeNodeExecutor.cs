using System.Text.Json;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class MergeNodeExecutor(PatchHoundDbContext dbContext) : IWorkflowNodeExecutor
{
    public string NodeType => "Merge";

    public async Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    )
    {
        execution.Start();

        // Parse the graph to find all edges that target this merge node
        WorkflowGraph? graph;
        try
        {
            graph = JsonSerializer.Deserialize<WorkflowGraph>(
                instance.WorkflowDefinition.GraphJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch
        {
            execution.Fail("Failed to parse workflow graph.");
            return new NodeExecutionResult(NodeExecutionOutcome.Failed, Error: "Invalid graph JSON");
        }

        if (graph is null)
        {
            execution.Fail("Workflow graph is null.");
            return new NodeExecutionResult(NodeExecutionOutcome.Failed, Error: "Null graph");
        }

        var incomingSourceNodeIds = graph.Edges
            .Where(e => e.Target == node.Id)
            .Select(e => e.Source)
            .ToList();

        if (incomingSourceNodeIds.Count == 0)
        {
            execution.Complete();
            return new NodeExecutionResult(NodeExecutionOutcome.Completed);
        }

        // Check that all incoming branches have completed their source nodes
        var completedCount = await dbContext.WorkflowNodeExecutions
            .Where(e =>
                e.WorkflowInstanceId == instance.Id
                && incomingSourceNodeIds.Contains(e.NodeId)
                && e.Status == WorkflowNodeExecutionStatus.Completed
            )
            .CountAsync(ct);

        if (completedCount >= incomingSourceNodeIds.Count)
        {
            execution.Complete();
            return new NodeExecutionResult(NodeExecutionOutcome.Completed);
        }

        // Not all branches done yet — wait
        execution.MarkWaitingForAction(Guid.Empty);
        return new NodeExecutionResult(NodeExecutionOutcome.WaitingForAction);
    }
}
