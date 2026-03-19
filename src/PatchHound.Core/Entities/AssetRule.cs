using System.Text.Json;
using PatchHound.Core.Models;

namespace PatchHound.Core.Entities;

public class AssetRule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; private set; }
    public string FilterDefinition { get; private set; } = null!;
    public string Operations { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastExecutedAt { get; private set; }
    public int? LastMatchCount { get; private set; }

    private AssetRule() { }

    public static AssetRule Create(
        Guid tenantId,
        string name,
        string? description,
        int priority,
        FilterNode filter,
        List<AssetRuleOperation> operations)
    {
        var now = DateTimeOffset.UtcNow;
        return new AssetRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            Priority = priority,
            Enabled = true,
            FilterDefinition = JsonSerializer.Serialize(filter, JsonOptions),
            Operations = JsonSerializer.Serialize(operations, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? description, bool enabled, FilterNode filter, List<AssetRuleOperation> operations)
    {
        Name = name;
        Description = description;
        Enabled = enabled;
        FilterDefinition = JsonSerializer.Serialize(filter, JsonOptions);
        Operations = JsonSerializer.Serialize(operations, JsonOptions);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetPriority(int priority)
    {
        Priority = priority;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordExecution(int matchCount)
    {
        LastExecutedAt = DateTimeOffset.UtcNow;
        LastMatchCount = matchCount;
    }

    public FilterNode ParseFilter() =>
        JsonSerializer.Deserialize<FilterNode>(FilterDefinition, JsonOptions)!;

    public List<AssetRuleOperation> ParseOperations() =>
        JsonSerializer.Deserialize<List<AssetRuleOperation>>(Operations, JsonOptions)!;
}
