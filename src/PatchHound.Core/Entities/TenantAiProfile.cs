using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class TenantAiProfile
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public TenantAiProviderType ProviderType { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsEnabled { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public string SystemPrompt { get; private set; } = string.Empty;
    public decimal Temperature { get; private set; }
    public decimal? TopP { get; private set; }
    public int MaxOutputTokens { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public string BaseUrl { get; private set; } = string.Empty;
    public string DeploymentName { get; private set; } = string.Empty;
    public string ApiVersion { get; private set; } = string.Empty;
    public string KeepAlive { get; private set; } = string.Empty;
    public string SecretRef { get; private set; } = string.Empty;
    public bool AllowExternalResearch { get; private set; }
    public TenantAiWebResearchMode WebResearchMode { get; private set; }
    public bool IncludeCitations { get; private set; }
    public int MaxResearchSources { get; private set; }
    public string AllowedDomains { get; private set; } = string.Empty;
    public DateTimeOffset? LastValidatedAt { get; private set; }
    public TenantAiProfileValidationStatus LastValidationStatus { get; private set; }
    public string LastValidationError { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantAiProfile() { }

    public static TenantAiProfile Create(
        Guid tenantId,
        string name,
        TenantAiProviderType providerType,
        bool isDefault,
        bool isEnabled,
        string model,
        string systemPrompt,
        decimal temperature,
        decimal? topP,
        int maxOutputTokens,
        int timeoutSeconds,
        string baseUrl = "",
        string deploymentName = "",
        string apiVersion = "",
        string keepAlive = "",
        string secretRef = "",
        bool allowExternalResearch = false,
        TenantAiWebResearchMode webResearchMode = TenantAiWebResearchMode.Disabled,
        bool includeCitations = true,
        int maxResearchSources = 5,
        string allowedDomains = ""
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantAiProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            ProviderType = providerType,
            IsDefault = isDefault,
            IsEnabled = isEnabled,
            Model = model.Trim(),
            SystemPrompt = systemPrompt.Trim(),
            Temperature = temperature,
            TopP = topP,
            MaxOutputTokens = maxOutputTokens,
            TimeoutSeconds = timeoutSeconds,
            BaseUrl = baseUrl.Trim(),
            DeploymentName = deploymentName.Trim(),
            ApiVersion = apiVersion.Trim(),
            KeepAlive = keepAlive.Trim(),
            SecretRef = secretRef.Trim(),
            AllowExternalResearch = allowExternalResearch,
            WebResearchMode = webResearchMode,
            IncludeCitations = includeCitations,
            MaxResearchSources = maxResearchSources,
            AllowedDomains = allowedDomains.Trim(),
            LastValidationStatus = TenantAiProfileValidationStatus.Unknown,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        bool isDefault,
        bool isEnabled,
        string model,
        string systemPrompt,
        decimal temperature,
        decimal? topP,
        int maxOutputTokens,
        int timeoutSeconds,
        string baseUrl,
        string deploymentName,
        string apiVersion,
        string keepAlive,
        string secretRef,
        bool allowExternalResearch,
        TenantAiWebResearchMode webResearchMode,
        bool includeCitations,
        int maxResearchSources,
        string allowedDomains
    )
    {
        Name = name.Trim();
        IsDefault = isDefault;
        IsEnabled = isEnabled;
        Model = model.Trim();
        SystemPrompt = systemPrompt.Trim();
        Temperature = temperature;
        TopP = topP;
        MaxOutputTokens = maxOutputTokens;
        TimeoutSeconds = timeoutSeconds;
        BaseUrl = baseUrl.Trim();
        DeploymentName = deploymentName.Trim();
        ApiVersion = apiVersion.Trim();
        KeepAlive = keepAlive.Trim();
        SecretRef = secretRef.Trim();
        AllowExternalResearch = allowExternalResearch;
        WebResearchMode = webResearchMode;
        IncludeCitations = includeCitations;
        MaxResearchSources = maxResearchSources;
        AllowedDomains = allowedDomains.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordValidation(
        TenantAiProfileValidationStatus status,
        string error
    )
    {
        LastValidatedAt = DateTimeOffset.UtcNow;
        LastValidationStatus = status;
        LastValidationError = error.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ResetValidation()
    {
        LastValidatedAt = null;
        LastValidationStatus = TenantAiProfileValidationStatus.Unknown;
        LastValidationError = string.Empty;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
