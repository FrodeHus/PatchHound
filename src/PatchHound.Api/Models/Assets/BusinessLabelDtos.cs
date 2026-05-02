namespace PatchHound.Api.Models.Assets;

public record BusinessLabelDto(
    Guid Id,
    string Name,
    string? Description,
    string? Color,
    bool IsActive,
    string WeightCategory,
    decimal RiskWeight,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record SaveBusinessLabelRequest(
    string Name,
    string? Description,
    string? Color,
    bool IsActive = true,
    string? WeightCategory = null
);
