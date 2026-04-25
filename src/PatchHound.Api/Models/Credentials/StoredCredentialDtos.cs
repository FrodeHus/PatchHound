namespace PatchHound.Api.Models.Credentials;

public record StoredCredentialDto(
    Guid Id,
    string Name,
    string Type,
    string TypeDisplayName,
    bool IsGlobal,
    string CredentialTenantId,
    string ClientId,
    IReadOnlyList<Guid> TenantIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateStoredCredentialRequest(
    string Name,
    string Type,
    bool IsGlobal,
    string CredentialTenantId,
    string ClientId,
    string ClientSecret,
    IReadOnlyList<Guid> TenantIds
);

public record UpdateStoredCredentialRequest(
    string Name,
    bool IsGlobal,
    string CredentialTenantId,
    string ClientId,
    string? ClientSecret,
    IReadOnlyList<Guid> TenantIds
);

public record AcceptedCredentialTypeDto(string Type, string DisplayName);
