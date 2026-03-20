using System.Text.Json;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class AssignGroupNodeExecutor(PatchHoundDbContext dbContext) : IWorkflowNodeExecutor
{
    public string NodeType => "AssignGroup";

    public async Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    )
    {
        execution.Start(JsonSerializer.Serialize(node.Data));

        if (!node.Data.TryGetValue("teamId", out var teamIdObj)
            || teamIdObj is not JsonElement teamIdElem
            || !Guid.TryParse(teamIdElem.GetString(), out var teamId))
        {
            execution.Fail("AssignGroup node is missing a valid teamId.");
            return new NodeExecutionResult(NodeExecutionOutcome.Failed, Error: "Missing teamId");
        }

        var actionType = WorkflowRequiredActionType.Review;
        if (node.Data.TryGetValue("requiredAction", out var actionObj)
            && actionObj is JsonElement actionElem
            && Enum.TryParse<WorkflowRequiredActionType>(actionElem.GetString(), true, out var parsed))
        {
            actionType = parsed;
        }

        string? instructions = null;
        if (node.Data.TryGetValue("instructions", out var instrObj)
            && instrObj is JsonElement instrElem
            && instrElem.ValueKind == JsonValueKind.String)
        {
            instructions = instrElem.GetString();
        }

        int? timeoutHours = null;
        if (node.Data.TryGetValue("timeoutHours", out var toObj)
            && toObj is JsonElement toElem
            && toElem.TryGetInt32(out var th))
        {
            timeoutHours = th;
        }

        var dueAt = timeoutHours.HasValue
            ? DateTimeOffset.UtcNow.AddHours(timeoutHours.Value)
            : (DateTimeOffset?)null;

        var tenantId = instance.TenantId
            ?? throw new InvalidOperationException("AssignGroup requires a tenant-scoped workflow.");

        var action = WorkflowAction.Create(
            instance.Id,
            execution.Id,
            tenantId,
            teamId,
            actionType,
            instructions,
            dueAt
        );

        dbContext.WorkflowActions.Add(action);
        execution.MarkWaitingForAction(teamId);

        await Task.CompletedTask;
        return new NodeExecutionResult(NodeExecutionOutcome.WaitingForAction);
    }
}
