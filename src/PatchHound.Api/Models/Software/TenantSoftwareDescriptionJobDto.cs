namespace PatchHound.Api.Models.Software;

public record TenantSoftwareDescriptionJobDto(
    Guid Id,
    Guid SoftwareProductId,
    string Status,
    string? Error,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);
