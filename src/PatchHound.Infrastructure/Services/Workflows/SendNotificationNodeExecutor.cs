using System.Text.Json;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class SendNotificationNodeExecutor(
    INotificationService notificationService,
    ILogger<SendNotificationNodeExecutor> logger
) : IWorkflowNodeExecutor
{
    public string NodeType => "SendNotification";

    public async Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    )
    {
        execution.Start(JsonSerializer.Serialize(node.Data));

        var tenantId = instance.TenantId;
        if (tenantId is null)
        {
            execution.Fail("SendNotification requires a tenant-scoped workflow.");
            return new NodeExecutionResult(NodeExecutionOutcome.Failed, Error: "Missing tenantId");
        }

        if (!node.Data.TryGetValue("teamId", out var teamIdObj)
            || teamIdObj is not JsonElement teamIdElem
            || !Guid.TryParse(teamIdElem.GetString(), out var teamId))
        {
            execution.Fail("SendNotification node is missing a valid teamId.");
            return new NodeExecutionResult(NodeExecutionOutcome.Failed, Error: "Missing teamId");
        }

        string title = "Workflow Notification";
        if (node.Data.TryGetValue("title", out var titleObj)
            && titleObj is JsonElement titleElem
            && titleElem.ValueKind == JsonValueKind.String)
        {
            title = titleElem.GetString() ?? title;
        }

        string body = $"Workflow instance {instance.Id} requires attention.";
        if (node.Data.TryGetValue("body", out var bodyObj)
            && bodyObj is JsonElement bodyElem
            && bodyElem.ValueKind == JsonValueKind.String)
        {
            body = bodyElem.GetString() ?? body;
        }

        try
        {
            await notificationService.SendToTeamAsync(
                teamId,
                tenantId.Value,
                NotificationType.WorkflowNotification,
                title,
                body,
                "WorkflowInstance",
                instance.Id,
                ct
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SendNotification node failed for workflow instance {InstanceId}", instance.Id);
            // Notification failure is non-fatal — complete and continue
        }

        execution.Complete();
        return new NodeExecutionResult(NodeExecutionOutcome.Completed);
    }
}
