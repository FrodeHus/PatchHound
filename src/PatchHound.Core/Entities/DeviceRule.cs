using System.Text.Json;
using PatchHound.Core.Models;

namespace PatchHound.Core.Entities;

public class DeviceRule
{
    public const int NameMaxLength = 256;
    public const int DescriptionMaxLength = 2048;

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

    private DeviceRule() { }

    public static DeviceRule Create(
        Guid tenantId,
        string name,
        string? description,
        int priority,
        FilterNode filter,
        List<AssetRuleOperation> operations)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        var (normalizedName, normalizedDescription, filterDefinition, operationsJson) =
            NormalizeAndValidate(name, description, filter, operations);

        var now = DateTimeOffset.UtcNow;
        return new DeviceRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = normalizedName,
            Description = normalizedDescription,
            Priority = priority,
            Enabled = true,
            FilterDefinition = filterDefinition,
            Operations = operationsJson,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? description, bool enabled, FilterNode filter, List<AssetRuleOperation> operations)
    {
        var (normalizedName, normalizedDescription, filterDefinition, operationsJson) =
            NormalizeAndValidate(name, description, filter, operations);

        Name = normalizedName;
        Description = normalizedDescription;
        Enabled = enabled;
        FilterDefinition = filterDefinition;
        Operations = operationsJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static (string name, string? description, string filterDefinition, string operationsJson) NormalizeAndValidate(
        string name,
        string? description,
        FilterNode filter,
        List<AssetRuleOperation> operations)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(operations);

        var normalizedName = name.Trim();
        var normalizedDescription = description?.Trim();

        if (normalizedName.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Name must be {NameMaxLength} characters or fewer.",
                nameof(name));
        }
        if (normalizedDescription is not null && normalizedDescription.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Description must be {DescriptionMaxLength} characters or fewer.",
                nameof(description));
        }

        return (
            normalizedName,
            normalizedDescription,
            JsonSerializer.Serialize(filter, JsonOptions),
            JsonSerializer.Serialize(operations, JsonOptions));
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
