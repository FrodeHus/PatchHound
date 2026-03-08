namespace PatchHound.Api.Models.System;

public record EnrichmentSourceDto(
    string Key,
    string DisplayName,
    bool Enabled,
    EnrichmentSourceCredentialsDto Credentials,
    EnrichmentSourceRuntimeDto Runtime
);

public record EnrichmentSourceCredentialsDto(bool HasSecret, string ApiBaseUrl);

public record EnrichmentSourceRuntimeDto(
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastSucceededAt,
    string LastStatus,
    string LastError
);

public record UpdateEnrichmentSourceRequest(
    string Key,
    string DisplayName,
    bool Enabled,
    UpdateEnrichmentSourceCredentialsRequest Credentials
);

public record UpdateEnrichmentSourceCredentialsRequest(string Secret, string ApiBaseUrl);
