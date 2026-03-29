namespace PatchHound.Api.Models.Integrations;

public record SentinelConnectorDto(
    bool Enabled,
    string DceEndpoint,
    string DcrImmutableId,
    string StreamName,
    string TenantId,
    string ClientId,
    bool HasSecret,
    DateTimeOffset? UpdatedAt
);
