namespace PatchHound.Api.Models.Software;

public record TenantSoftwareDescriptionJobDto(
    Guid Id,
    Guid TenantSoftwareId,
    string Status,
    string? Error,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);
