namespace PatchHound.Api.Models.Integrations;

public record SentinelConnectorDto(
    bool Enabled,
    string DceEndpoint,
    string DcrImmutableId,
    string StreamName,
    Guid? StoredCredentialId,
    string[] AcceptedCredentialTypes,
    DateTimeOffset? UpdatedAt
);
