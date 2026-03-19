using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchHound.Core.Models;

[JsonConverter(typeof(FilterNodeConverter))]
public abstract record FilterNode(string Type);

public record FilterGroup(
    string Operator,
    List<FilterNode> Conditions
) : FilterNode("group");

public record FilterCondition(
    string Field,
    [property: JsonPropertyName("operator")] string Op,
    string Value
) : FilterNode("condition");

public record AssetRuleOperation(
    string Type,
    Dictionary<string, string> Parameters
);

public class FilterNodeConverter : JsonConverter<FilterNode>
{
    private static readonly JsonSerializerOptions InnerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public override FilterNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        var raw = root.GetRawText();
        return type switch
        {
            "group" => DeserializeGroup(raw),
            "condition" => JsonSerializer.Deserialize<FilterCondition>(raw, InnerOptions),
            _ => throw new JsonException($"Unknown filter node type: {type}")
        };
    }

    private static FilterGroup DeserializeGroup(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var op = root.GetProperty("operator").GetString() ?? "AND";
        var conditions = new List<FilterNode>();
        if (root.TryGetProperty("conditions", out var conditionsElement))
        {
            foreach (var item in conditionsElement.EnumerateArray())
            {
                var itemType = item.GetProperty("type").GetString();
                var itemRaw = item.GetRawText();
                conditions.Add(itemType switch
                {
                    "group" => DeserializeGroup(itemRaw),
                    "condition" => JsonSerializer.Deserialize<FilterCondition>(itemRaw, InnerOptions)!,
                    _ => throw new JsonException($"Unknown filter node type: {itemType}")
                });
            }
        }
        return new FilterGroup(op, conditions);
    }

    public override void Write(Utf8JsonWriter writer, FilterNode value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case FilterGroup group:
                writer.WriteStartObject();
                writer.WriteString("type", "group");
                writer.WriteString("operator", group.Operator);
                writer.WritePropertyName("conditions");
                writer.WriteStartArray();
                foreach (var condition in group.Conditions)
                    Write(writer, condition, options);
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            case FilterCondition condition:
                writer.WriteStartObject();
                writer.WriteString("type", "condition");
                writer.WriteString("field", condition.Field);
                writer.WriteString("operator", condition.Op);
                writer.WriteString("value", condition.Value);
                writer.WriteEndObject();
                break;
        }
    }
}
