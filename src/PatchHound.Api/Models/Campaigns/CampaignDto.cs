namespace PatchHound.Api.Models.Campaigns;

public record CampaignDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    int VulnerabilityCount,
    int TotalTasks,
    int CompletedTasks
);

public record CampaignDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    int VulnerabilityCount,
    int TotalTasks,
    int CompletedTasks,
    IReadOnlyList<Guid> VulnerabilityIds
);

public record CreateCampaignRequest(string Name, string? Description);

public record UpdateCampaignRequest(string? Name, string? Description);

public record LinkVulnerabilitiesRequest(List<Guid> VulnerabilityIds);

public record BulkAssignCampaignRequest(Guid AssigneeId);

public record CampaignFilterQuery(string? Status, Guid? TenantId);
