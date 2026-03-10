namespace PatchHound.Api.Models.Tasks;

public record RemediationTaskDto(
    Guid Id,
    Guid TenantVulnerabilityId,
    Guid AssetId,
    string VulnerabilityTitle,
    string AssetName,
    string Status,
    string? Justification,
    DateTimeOffset DueDate,
    DateTimeOffset CreatedAt,
    bool IsOverdue
);
