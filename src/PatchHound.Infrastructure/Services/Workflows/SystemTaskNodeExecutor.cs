using System.Text.Json;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class SystemTaskNodeExecutor(
    ILogger<SystemTaskNodeExecutor> logger
) : IWorkflowNodeExecutor
{
    public string NodeType => "SystemTask";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    )
    {
        execution.Start(JsonSerializer.Serialize(node.Data));

        var taskType = "unknown";
        if (node.Data.TryGetValue("taskType", out var ttObj)
            && ttObj is JsonElement ttElem
            && ttElem.ValueKind == JsonValueKind.String)
        {
            taskType = ttElem.GetString() ?? "unknown";
        }

        // System task execution is deferred to the WorkflowWorker background service.
        // The engine marks the node as Running and the worker picks it up.
        logger.LogInformation(
            "SystemTask node {NodeId} of type '{TaskType}' queued for workflow instance {InstanceId}",
            node.Id, taskType, instance.Id
        );

        // Leave status as Running — the WorkflowWorker will complete it.
        return Task.FromResult(new NodeExecutionResult(NodeExecutionOutcome.WaitingForAction));
    }
}
