namespace PatchHound.Api.Models.CloudApplications;

public record CloudApplicationListItemDto(
    Guid Id,
    string Name,
    string? Description,
    int CredentialCount,
    int ExpiredCredentialCount,
    int ExpiringCredentialCount,
    DateTimeOffset? NextExpiryAt
);

public record CloudApplicationCredentialDto(
    Guid Id,
    string ExternalId,
    string Type,
    string? DisplayName,
    DateTimeOffset ExpiresAt
);
