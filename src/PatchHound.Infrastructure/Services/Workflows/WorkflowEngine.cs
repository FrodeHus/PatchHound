using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class WorkflowEngine(
    PatchHoundDbContext dbContext,
    IEnumerable<IWorkflowNodeExecutor> nodeExecutors,
    ILogger<WorkflowEngine> logger
) : IWorkflowEngine
{
    private readonly Dictionary<string, IWorkflowNodeExecutor> _executors =
        nodeExecutors.ToDictionary(e => e.NodeType, StringComparer.OrdinalIgnoreCase);

    public async Task<WorkflowInstance> StartWorkflowAsync(
        Guid definitionId,
        string contextJson,
        Guid? triggeredBy,
        CancellationToken ct
    )
    {
        var definition = await dbContext.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Id == definitionId && d.Status == WorkflowDefinitionStatus.Published, ct)
            ?? throw new InvalidOperationException($"Published workflow definition {definitionId} not found.");

        var instance = WorkflowInstance.Create(definition, contextJson, triggeredBy);
        dbContext.WorkflowInstances.Add(instance);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation(
            "Started workflow instance {InstanceId} from definition {DefinitionId} v{Version}",
            instance.Id, definition.Id, definition.Version
        );

        await ExecuteGraphAsync(instance, definition, ct);
        return instance;
    }

    public async Task ResumeWorkflowAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await LoadInstanceWithExecutions(instanceId, ct);

        if (instance.Status is WorkflowInstanceStatus.Completed
            or WorkflowInstanceStatus.Cancelled
            or WorkflowInstanceStatus.Failed)
        {
            return;
        }

        instance.MarkRunning();
        await ExecuteGraphAsync(instance, instance.WorkflowDefinition, ct);
    }

    public async Task CompleteActionAsync(
        Guid actionId,
        Guid userId,
        string? responseJson,
        CancellationToken ct
    )
    {
        var action = await dbContext.WorkflowActions
            .Include(a => a.NodeExecution)
            .FirstOrDefaultAsync(a => a.Id == actionId, ct)
            ?? throw new InvalidOperationException($"Workflow action {actionId} not found.");

        if (action.Status != WorkflowActionStatus.Pending)
            throw new InvalidOperationException($"Action {actionId} is already {action.Status}.");

        action.Complete(userId, responseJson);
        action.NodeExecution.Complete(responseJson, userId);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Action {ActionId} completed by user {UserId}", actionId, userId);

        await ResumeWorkflowAsync(action.WorkflowInstanceId, ct);
    }

    public async Task RejectActionAsync(
        Guid actionId,
        Guid userId,
        string? responseJson,
        CancellationToken ct
    )
    {
        var action = await dbContext.WorkflowActions
            .Include(a => a.NodeExecution)
            .FirstOrDefaultAsync(a => a.Id == actionId, ct)
            ?? throw new InvalidOperationException($"Workflow action {actionId} not found.");

        if (action.Status != WorkflowActionStatus.Pending)
            throw new InvalidOperationException($"Action {actionId} is already {action.Status}.");

        action.Reject(userId, responseJson);
        action.NodeExecution.Fail("Rejected by user");

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Action {ActionId} rejected by user {UserId}", actionId, userId);

        // Mark instance as failed when an action is rejected
        var instance = await LoadInstanceWithExecutions(action.WorkflowInstanceId, ct);
        instance.Fail($"Action rejected at node {action.NodeExecution.NodeId}");
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task CancelWorkflowAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await dbContext.WorkflowInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled)
            return;

        instance.Cancel();

        // Cancel any pending actions
        var pendingActions = await dbContext.WorkflowActions
            .Where(a => a.WorkflowInstanceId == instanceId && a.Status == WorkflowActionStatus.Pending)
            .ToListAsync(ct);

        foreach (var action in pendingActions)
            action.TimeOut();

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("Workflow instance {InstanceId} cancelled", instanceId);
    }

    private async Task ExecuteGraphAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        CancellationToken ct
    )
    {
        var graph = JsonSerializer.Deserialize<WorkflowGraph>(definition.GraphJson)
            ?? throw new InvalidOperationException("Failed to deserialize workflow graph.");

        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        var outgoingEdges = graph.Edges
            .GroupBy(e => e.Source)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Gather already-executed node IDs for this instance
        var existingExecutions = instance.NodeExecutions
            .ToDictionary(e => e.NodeId);

        // Find the start node
        var startNode = graph.Nodes.FirstOrDefault(n =>
            n.Type.Equals("Start", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Workflow graph has no Start node.");

        // BFS-style traversal starting from Start node
        var queue = new Queue<string>();
        var visited = new HashSet<string>();

        // On fresh start, begin from Start node. On resume, find nodes ready to execute.
        if (!existingExecutions.Any())
        {
            queue.Enqueue(startNode.Id);
        }
        else
        {
            // Resume: find nodes whose execution just completed or is pending
            foreach (var exec in existingExecutions.Values)
            {
                if (exec.Status == WorkflowNodeExecutionStatus.Completed)
                {
                    // Enqueue successor nodes
                    if (outgoingEdges.TryGetValue(exec.NodeId, out var edges))
                    {
                        foreach (var edge in edges)
                        {
                            if (!existingExecutions.TryGetValue(edge.Target, out var targetExec)
                                || targetExec.Status == WorkflowNodeExecutionStatus.Pending)
                            {
                                queue.Enqueue(edge.Target);
                            }
                        }
                    }
                }
            }

            // Also re-check merge nodes that were waiting
            foreach (var exec in existingExecutions.Values)
            {
                if (exec.Status == WorkflowNodeExecutionStatus.WaitingForAction
                    && nodeMap.TryGetValue(exec.NodeId, out var n)
                    && n.Type.Equals("Merge", StringComparison.OrdinalIgnoreCase))
                {
                    queue.Enqueue(exec.NodeId);
                }
            }
        }

        var hasWaiting = false;

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!visited.Add(nodeId))
                continue;

            if (!nodeMap.TryGetValue(nodeId, out var node))
            {
                logger.LogWarning("Node {NodeId} not found in graph for instance {InstanceId}", nodeId, instance.Id);
                continue;
            }

            // Get or create execution record
            if (!existingExecutions.TryGetValue(nodeId, out var execution))
            {
                execution = instance.AddNodeExecution(nodeId, node.Type);
                existingExecutions[nodeId] = execution;
            }
            else if (execution.Status is WorkflowNodeExecutionStatus.Completed
                     or WorkflowNodeExecutionStatus.Failed
                     or WorkflowNodeExecutionStatus.Skipped)
            {
                // Already processed — just enqueue successors for completed
                if (execution.Status == WorkflowNodeExecutionStatus.Completed)
                    EnqueueSuccessors(nodeId, null, outgoingEdges, queue);
                continue;
            }

            if (!_executors.TryGetValue(node.Type, out var executor))
            {
                execution.Fail($"No executor registered for node type '{node.Type}'.");
                logger.LogError("No executor for node type '{NodeType}'", node.Type);
                instance.Fail($"Unsupported node type: {node.Type}");
                await dbContext.SaveChangesAsync(ct);
                return;
            }

            NodeExecutionResult result;
            try
            {
                result = await executor.ExecuteAsync(instance, execution, node, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Node {NodeId} execution failed", nodeId);
                execution.Fail(ex.Message);
                instance.Fail($"Node {nodeId} threw: {ex.Message}");
                await dbContext.SaveChangesAsync(ct);
                return;
            }

            await dbContext.SaveChangesAsync(ct);

            switch (result.Outcome)
            {
                case NodeExecutionOutcome.Completed:
                    // End node → mark instance complete
                    if (node.Type.Equals("End", StringComparison.OrdinalIgnoreCase))
                    {
                        instance.Complete();
                        await dbContext.SaveChangesAsync(ct);
                        logger.LogInformation("Workflow instance {InstanceId} completed", instance.Id);
                        return;
                    }
                    EnqueueSuccessors(nodeId, result.NextEdgeLabel, outgoingEdges, queue);
                    break;

                case NodeExecutionOutcome.WaitingForAction:
                    hasWaiting = true;
                    break;

                case NodeExecutionOutcome.Failed:
                    instance.Fail(result.Error ?? $"Node {nodeId} failed.");
                    await dbContext.SaveChangesAsync(ct);
                    return;
            }
        }

        if (hasWaiting)
        {
            instance.MarkWaitingForAction();
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private static void EnqueueSuccessors(
        string sourceNodeId,
        string? edgeLabel,
        Dictionary<string, List<WorkflowGraphEdge>> outgoingEdges,
        Queue<string> queue
    )
    {
        if (!outgoingEdges.TryGetValue(sourceNodeId, out var edges))
            return;

        foreach (var edge in edges)
        {
            // If the executor returned a label (e.g. condition "true"/"false"),
            // only follow edges matching that label or sourceHandle.
            if (edgeLabel is not null)
            {
                var edgeMatch = string.Equals(edge.Label, edgeLabel, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(edge.SourceHandle, edgeLabel, StringComparison.OrdinalIgnoreCase);
                if (!edgeMatch)
                    continue;
            }

            queue.Enqueue(edge.Target);
        }
    }

    private async Task<WorkflowInstance> LoadInstanceWithExecutions(Guid instanceId, CancellationToken ct)
    {
        return await dbContext.WorkflowInstances
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.NodeExecutions)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");
    }
}
