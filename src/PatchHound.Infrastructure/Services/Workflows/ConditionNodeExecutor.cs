using System.Text.Json;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class ConditionNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => "Condition";

    public Task<NodeExecutionResult> ExecuteAsync(
        WorkflowInstance instance,
        WorkflowNodeExecution execution,
        WorkflowGraphNode node,
        CancellationToken ct
    )
    {
        execution.Start(instance.ContextJson);

        if (!node.Data.TryGetValue("rules", out var rulesObj)
            || rulesObj is not JsonElement rulesElem)
        {
            execution.Fail("Condition node is missing rules.");
            return Task.FromResult(new NodeExecutionResult(NodeExecutionOutcome.Failed, Error: "Missing rules"));
        }

        var rules = JsonSerializer.Deserialize<List<ConditionRule>>(rulesElem.GetRawText())
            ?? [];

        JsonDocument? ctx = null;
        try
        {
            ctx = JsonDocument.Parse(instance.ContextJson);
        }
        catch
        {
            execution.Fail("Invalid workflow context JSON.");
            return Task.FromResult(new NodeExecutionResult(NodeExecutionOutcome.Failed, Error: "Invalid context JSON"));
        }

        var allMatch = true;
        foreach (var rule in rules)
        {
            if (!EvaluateRule(ctx.RootElement, rule))
            {
                allMatch = false;
                break;
            }
        }

        ctx.Dispose();

        var edgeLabel = allMatch ? "true" : "false";
        execution.Complete(JsonSerializer.Serialize(new { result = edgeLabel }));
        return Task.FromResult(new NodeExecutionResult(NodeExecutionOutcome.Completed, NextEdgeLabel: edgeLabel));
    }

    private static bool EvaluateRule(JsonElement root, ConditionRule rule)
    {
        if (!TryGetNestedProperty(root, rule.Field, out var prop))
            return false;

        var actual = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString() ?? "",
            JsonValueKind.Number => prop.GetDouble().ToString("G"),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => prop.GetRawText(),
        };

        return rule.Operator.ToLowerInvariant() switch
        {
            "eq" or "==" or "equals" => string.Equals(actual, rule.Value, StringComparison.OrdinalIgnoreCase),
            "neq" or "!=" or "notequals" => !string.Equals(actual, rule.Value, StringComparison.OrdinalIgnoreCase),
            "gt" or ">" => double.TryParse(actual, out var a) && double.TryParse(rule.Value, out var b) && a > b,
            "gte" or ">=" => double.TryParse(actual, out var a2) && double.TryParse(rule.Value, out var b2) && a2 >= b2,
            "lt" or "<" => double.TryParse(actual, out var a3) && double.TryParse(rule.Value, out var b3) && a3 < b3,
            "lte" or "<=" => double.TryParse(actual, out var a4) && double.TryParse(rule.Value, out var b4) && a4 <= b4,
            "contains" => actual.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool TryGetNestedProperty(JsonElement root, string path, out JsonElement result)
    {
        result = root;
        foreach (var segment in path.Split('.'))
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(segment, out var next))
            {
                result = default;
                return false;
            }
            result = next;
        }
        return true;
    }
}
