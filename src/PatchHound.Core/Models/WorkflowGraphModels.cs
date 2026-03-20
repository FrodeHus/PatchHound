using System.Text.Json.Serialization;

namespace PatchHound.Core.Models;

public sealed class WorkflowGraph
{
    [JsonPropertyName("nodes")]
    public List<WorkflowGraphNode> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<WorkflowGraphEdge> Edges { get; set; } = [];
}

public sealed class WorkflowGraphNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("position")]
    public WorkflowGraphPosition Position { get; set; } = new();

    [JsonPropertyName("data")]
    public Dictionary<string, object?> Data { get; set; } = new();
}

public sealed class WorkflowGraphPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class WorkflowGraphEdge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("source")]
    public string Source { get; set; } = null!;

    [JsonPropertyName("target")]
    public string Target { get; set; } = null!;

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("sourceHandle")]
    public string? SourceHandle { get; set; }
}

public sealed class ConditionRule
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = null!;

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = null!;

    [JsonPropertyName("value")]
    public string Value { get; set; } = null!;
}
