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
    bool AllowExternalResearch,
    string WebResearchMode,
    bool IncludeCitations,
    int MaxResearchSources,
    string AllowedDomains,
    string ResearchSourceKey,
    bool HasSecret,
    DateTimeOffset? LastValidatedAt,
    string LastValidationStatus,
    string LastValidationError,
    int? NumCtx,
    string ResponseFormat,
    bool AllowOperationalContext,
    string OperationalContextMode,
    int MaxOperationalContextTokens,
    bool IncludeDeviceNamesInContext,
    bool IncludeUserNamesInContext
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
    bool AllowExternalResearch,
    string WebResearchMode,
    bool IncludeCitations,
    int MaxResearchSources,
    string AllowedDomains,
    string ApiKey,
    int? NumCtx,
    string? ResponseFormat,
    string ResearchSourceKey = "",
    bool AllowOperationalContext = false,
    string OperationalContextMode = "StructuredOnly",
    int MaxOperationalContextTokens = 3000,
    bool IncludeDeviceNamesInContext = true,
    bool IncludeUserNamesInContext = false
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
