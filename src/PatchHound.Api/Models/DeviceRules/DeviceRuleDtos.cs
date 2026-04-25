using System.Text.Json;

namespace PatchHound.Api.Models.DeviceRules;

public record DeviceRuleDto(
    Guid Id,
    string AssetType,
    string Name,
    string? Description,
    int Priority,
    bool Enabled,
    JsonElement FilterDefinition,
    JsonElement Operations,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastExecutedAt,
    int? LastMatchCount
);

public record CreateDeviceRuleRequest(
    string Name,
    string? Description,
    string AssetType,
    JsonElement FilterDefinition,
    JsonElement Operations
);

public record UpdateDeviceRuleRequest(
    string Name,
    string? Description,
    bool Enabled,
    string AssetType,
    JsonElement FilterDefinition,
    JsonElement Operations
);

public record PreviewDeviceRuleFilterRequest(string AssetType, JsonElement FilterDefinition);

public record ReorderDeviceRulesRequest(List<Guid> RuleIds);

public record DeviceRulePreviewDto(int Count, List<DevicePreviewItemDto> Samples);

public record DevicePreviewItemDto(Guid Id, string Name);
