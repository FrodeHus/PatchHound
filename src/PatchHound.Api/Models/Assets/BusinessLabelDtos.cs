namespace PatchHound.Api.Models.Assets;

public record BusinessLabelDto(
    Guid Id,
    string Name,
    string? Description,
    string? Color,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record SaveBusinessLabelRequest(
    string Name,
    string? Description,
    string? Color,
    bool IsActive = true
);
