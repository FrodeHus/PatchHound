namespace PatchHound.Api.Models.Settings;

public record TenantAiProfileDto(
    Guid Id,
    string Name,
    string ProviderType,
    bool IsDefault,
    bool IsEnabled,
    string Model,
    string SystemPrompt,
    decimal Temperature,
    decimal? TopP,
    int MaxOutputTokens,
    int TimeoutSeconds,
    string BaseUrl,
    string DeploymentName,
    string ApiVersion,
    string KeepAlive,
    bool HasSecret,
    DateTimeOffset? LastValidatedAt,
    string LastValidationStatus,
    string LastValidationError
);

public record SaveTenantAiProfileRequest(
    string Name,
    string ProviderType,
    bool IsDefault,
    bool IsEnabled,
    string Model,
    string SystemPrompt,
    decimal Temperature,
    decimal? TopP,
    int MaxOutputTokens,
    int TimeoutSeconds,
    string BaseUrl,
    string DeploymentName,
    string ApiVersion,
    string KeepAlive,
    string ApiKey
);

public record TenantAiProfileValidationResultDto(
    Guid Id,
    string ValidationStatus,
    string ValidationError,
    DateTimeOffset? LastValidatedAt
);

public record TenantAiProfileModelsDto(
    Guid Id,
    IReadOnlyList<string> Models
);
