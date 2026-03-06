namespace Vigil.Api.Models.Tasks;

public record RemediationTaskDto(
    Guid Id,
    Guid VulnerabilityId,
    Guid AssetId,
    string VulnerabilityTitle,
    string AssetName,
    string Status,
    string? Justification,
    DateTimeOffset DueDate,
    DateTimeOffset CreatedAt,
    bool IsOverdue
);
