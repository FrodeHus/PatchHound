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

public record CloudApplicationDetailDto(
    Guid Id,
    string ExternalId,
    string? AppId,
    string Name,
    string? Description,
    bool IsFallbackPublicClient,
    IReadOnlyList<string> RedirectUris,
    Guid? OwnerTeamId,
    string? OwnerTeamName,
    IReadOnlyList<CloudApplicationCredentialDto> Credentials
);

public record CloudApplicationCredentialDto(
    Guid Id,
    string ExternalId,
    string Type,
    string? DisplayName,
    DateTimeOffset ExpiresAt
);

public record AssignCloudApplicationOwnerRequest(Guid? TeamId);
