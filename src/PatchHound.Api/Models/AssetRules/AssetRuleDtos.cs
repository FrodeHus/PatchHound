using System.Text.Json;

namespace PatchHound.Api.Models.AssetRules;

public record AssetRuleDto(
    Guid Id,
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

public record CreateAssetRuleRequest(
    string Name,
    string? Description,
    JsonElement FilterDefinition,
    JsonElement Operations
);

public record UpdateAssetRuleRequest(
    string Name,
    string? Description,
    bool Enabled,
    JsonElement FilterDefinition,
    JsonElement Operations
);

public record PreviewFilterRequest(JsonElement FilterDefinition);

public record ReorderRulesRequest(List<Guid> RuleIds);

public record AssetRulePreviewDto(int Count, List<AssetPreviewItemDto> Samples);

public record AssetPreviewItemDto(Guid Id, string Name, string AssetType);
