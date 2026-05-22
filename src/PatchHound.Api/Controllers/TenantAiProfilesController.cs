using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Settings;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/settings/ai")]
[Authorize(Policy = Policies.ConfigureTenant)]
public class TenantAiProfilesController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantAiConfigurationResolver _resolver;
    private readonly IEnumerable<IAiReportProvider> _providers;

    public TenantAiProfilesController(
        PatchHoundDbContext dbContext,
        ISecretStore secretStore,
        ITenantContext tenantContext,
        ITenantAiConfigurationResolver resolver,
        IEnumerable<IAiReportProvider> providers
    )
    {
        _dbContext = dbContext;
        _secretStore = secretStore;
        _tenantContext = tenantContext;
        _resolver = resolver;
        _providers = providers;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TenantAiProfileDto>>> List(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var profiles = await _dbContext
            .TenantAiProfiles.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .ToListAsync(ct);

        return Ok(profiles.Select(MapDto).ToList());
    }

    [HttpPost("profiles")]
    public async Task<ActionResult<TenantAiProfileDto>> Create(
        [FromBody] SaveTenantAiProfileRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        if (!TryParseProviderType(request.ProviderType, out var providerType))
        {
            return BadRequest(new ProblemDetails { Title = "Unsupported AI provider type." });
        }

        var validationProblem = ValidateRequest(request, providerType, isUpdate: false);
        if (validationProblem is not null)
        {
            return BadRequest(validationProblem);
        }

        var researchSourceProblem = await ValidateResearchSourceAsync(request, ct);
        if (researchSourceProblem is not null)
        {
            return BadRequest(researchSourceProblem);
        }

        var profile = TenantAiProfile.Create(
            tenantId,
            request.Name,
            providerType,
            request.IsDefault,
            request.IsEnabled,
            request.Model,
            request.SystemPrompt,
            request.Temperature,
            request.TopP,
            request.MaxOutputTokens,
            request.TimeoutSeconds,
            request.BaseUrl,
            request.DeploymentName,
            request.ApiVersion,
            request.KeepAlive,
            allowExternalResearch: request.AllowExternalResearch,
            webResearchMode: ResolveWebResearchMode(request),
            includeCitations: request.IncludeCitations,
            maxResearchSources: request.MaxResearchSources,
            allowedDomains: request.AllowedDomains,
            numCtx: request.NumCtx,
            responseFormat: ResolveResponseFormat(request.ResponseFormat),
            researchSourceKey: ResolveResearchSourceKey(request)
        );

        var secretRef = BuildSecretRef(tenantId, profile.Id);
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            await _secretStore.PutSecretAsync(
                secretRef,
                new Dictionary<string, string> { ["apiKey"] = request.ApiKey.Trim() },
                ct
            );
            profile.Update(
                request.Name,
                request.IsDefault,
                request.IsEnabled,
                request.Model,
                request.SystemPrompt,
                request.Temperature,
                request.TopP,
                request.MaxOutputTokens,
                request.TimeoutSeconds,
                request.BaseUrl,
                request.DeploymentName,
                request.ApiVersion,
                request.KeepAlive,
                secretRef,
                request.AllowExternalResearch,
                ResolveWebResearchMode(request),
                request.IncludeCitations,
                request.MaxResearchSources,
                request.AllowedDomains,
                request.NumCtx,
                ResolveResponseFormat(request.ResponseFormat),
                ResolveResearchSourceKey(request)
            );
        }

        var shouldBeDefault =
            request.IsEnabled
            && (request.IsDefault || !await _dbContext.TenantAiProfiles.AnyAsync(item => item.TenantId == tenantId, ct));

        if (shouldBeDefault)
        {
            await ClearDefaultAsync(tenantId, ct);
            profile.Update(
                request.Name,
                true,
                request.IsEnabled,
                request.Model,
                request.SystemPrompt,
                request.Temperature,
                request.TopP,
                request.MaxOutputTokens,
                request.TimeoutSeconds,
                request.BaseUrl,
                request.DeploymentName,
                request.ApiVersion,
                request.KeepAlive,
                profile.SecretRef,
                request.AllowExternalResearch,
                ResolveWebResearchMode(request),
                request.IncludeCitations,
                request.MaxResearchSources,
                request.AllowedDomains,
                request.NumCtx,
                ResolveResponseFormat(request.ResponseFormat),
                ResolveResearchSourceKey(request)
            );
        }

        await _dbContext.TenantAiProfiles.AddAsync(profile, ct);
        await _dbContext.SaveChangesAsync(ct);

        return Ok(MapDto(profile));
    }

    [HttpPut("profiles/{id:guid}")]
    public async Task<ActionResult<TenantAiProfileDto>> Update(
        Guid id,
        [FromBody] SaveTenantAiProfileRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var profile = await _dbContext.TenantAiProfiles.FirstOrDefaultAsync(
            item => item.Id == id && item.TenantId == tenantId,
            ct
        );

        if (profile is null)
        {
            return NotFound(new ProblemDetails { Title = "AI profile not found." });
        }

        if (!TryParseProviderType(request.ProviderType, out var providerType))
        {
            return BadRequest(new ProblemDetails { Title = "Unsupported AI provider type." });
        }

        if (providerType != profile.ProviderType)
        {
            return BadRequest(new ProblemDetails { Title = "Provider type cannot be changed for an existing profile." });
        }

        var validationProblem = ValidateRequest(request, providerType, isUpdate: true);
        if (validationProblem is not null)
        {
            return BadRequest(validationProblem);
        }

        var researchSourceProblem = await ValidateResearchSourceAsync(request, ct);
        if (researchSourceProblem is not null)
        {
            return BadRequest(researchSourceProblem);
        }

        var secretRef = profile.SecretRef;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            secretRef = string.IsNullOrWhiteSpace(secretRef) ? BuildSecretRef(tenantId, profile.Id) : secretRef;
            await _secretStore.PutSecretAsync(
                secretRef,
                new Dictionary<string, string> { ["apiKey"] = request.ApiKey.Trim() },
                ct
            );
        }

        if (request.IsDefault)
        {
            await ClearDefaultAsync(tenantId, ct, profile.Id);
        }

        profile.Update(
            request.Name,
            request.IsDefault,
            request.IsEnabled,
            request.Model,
            request.SystemPrompt,
            request.Temperature,
            request.TopP,
            request.MaxOutputTokens,
            request.TimeoutSeconds,
            request.BaseUrl,
            request.DeploymentName,
            request.ApiVersion,
            request.KeepAlive,
            secretRef,
            request.AllowExternalResearch,
            ResolveWebResearchMode(request),
            request.IncludeCitations,
            request.MaxResearchSources,
            request.AllowedDomains,
            request.NumCtx,
            ResolveResponseFormat(request.ResponseFormat),
            ResolveResearchSourceKey(request)
        );
        profile.ResetValidation();

        await _dbContext.SaveChangesAsync(ct);
        return Ok(MapDto(profile));
    }

    [HttpPost("profiles/{id:guid}/set-default")]
    public async Task<ActionResult<TenantAiProfileDto>> SetDefault(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var profile = await _dbContext.TenantAiProfiles.FirstOrDefaultAsync(
            item => item.Id == id && item.TenantId == tenantId,
            ct
        );

        if (profile is null)
        {
            return NotFound(new ProblemDetails { Title = "AI profile not found." });
        }

        if (!profile.IsEnabled)
        {
            return BadRequest(new ProblemDetails { Title = "Only enabled AI profiles can be set as default." });
        }

        await ClearDefaultAsync(tenantId, ct, profile.Id);
        profile.Update(
            profile.Name,
            true,
            profile.IsEnabled,
            profile.Model,
            profile.SystemPrompt,
            profile.Temperature,
            profile.TopP,
            profile.MaxOutputTokens,
            profile.TimeoutSeconds,
            profile.BaseUrl,
            profile.DeploymentName,
            profile.ApiVersion,
            profile.KeepAlive,
            profile.SecretRef,
            profile.AllowExternalResearch,
            profile.WebResearchMode,
            profile.IncludeCitations,
            profile.MaxResearchSources,
            profile.AllowedDomains,
            profile.NumCtx,
            profile.ResponseFormat,
            profile.ResearchSourceKey
        );

        await _dbContext.SaveChangesAsync(ct);
        return Ok(MapDto(profile));
    }

    [HttpPost("profiles/{id:guid}/validate")]
    public async Task<ActionResult<TenantAiProfileValidationResultDto>> ValidateProfile(
        Guid id,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var resolveResult = await _resolver.ResolveByIdAsync(tenantId, id, ct);
        if (!resolveResult.IsSuccess)
        {
            return NotFound(new ProblemDetails { Title = resolveResult.Error });
        }

        var resolved = resolveResult.Value;
        var profile = await _dbContext.TenantAiProfiles.FirstAsync(
            item => item.Id == id && item.TenantId == tenantId,
            ct
        );

        var provider = _providers.FirstOrDefault(item => item.ProviderType == resolved.Profile.ProviderType);
        if (provider is null)
        {
            profile.RecordValidation(TenantAiProfileValidationStatus.Invalid, "No provider implementation is registered for this AI profile.");
            await _dbContext.SaveChangesAsync(ct);
            return Ok(MapValidationDto(profile));
        }

        var validation = await provider.ValidateAsync(resolved, ct);
        profile.RecordValidation(
            validation.IsSuccess ? TenantAiProfileValidationStatus.Valid : TenantAiProfileValidationStatus.Invalid,
            validation.Error
        );
        await _dbContext.SaveChangesAsync(ct);

        return Ok(MapValidationDto(profile));
    }

    [HttpPost("profiles/{id:guid}/models")]
    public async Task<ActionResult<TenantAiProfileModelsDto>> ListAvailableModels(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var resolveResult = await _resolver.ResolveByIdAsync(tenantId, id, ct);
        if (!resolveResult.IsSuccess)
        {
            return NotFound(new ProblemDetails { Title = resolveResult.Error });
        }

        var resolved = resolveResult.Value;
        var provider = _providers.FirstOrDefault(item => item.ProviderType == resolved.Profile.ProviderType);
        if (provider is null)
        {
            return BadRequest(new ProblemDetails { Title = "No provider implementation is registered for this AI profile." });
        }

        var modelsResult = await provider.ListAvailableModelsAsync(resolved, ct);
        if (!modelsResult.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = modelsResult.Error });
        }

        return Ok(new TenantAiProfileModelsDto(id, modelsResult.Value));
    }

    private async Task ClearDefaultAsync(Guid tenantId, CancellationToken ct, Guid? exceptId = null)
    {
        var profiles = await _dbContext.TenantAiProfiles
            .Where(item => item.TenantId == tenantId && item.IsDefault)
            .ToListAsync(ct);

        foreach (var profile in profiles)
        {
            if (exceptId.HasValue && profile.Id == exceptId.Value)
            {
                continue;
            }

            profile.Update(
                profile.Name,
                false,
                profile.IsEnabled,
                profile.Model,
                profile.SystemPrompt,
                profile.Temperature,
                profile.TopP,
                profile.MaxOutputTokens,
                profile.TimeoutSeconds,
                profile.BaseUrl,
                profile.DeploymentName,
                profile.ApiVersion,
                profile.KeepAlive,
                profile.SecretRef,
                profile.AllowExternalResearch,
                profile.WebResearchMode,
                profile.IncludeCitations,
                profile.MaxResearchSources,
                profile.AllowedDomains,
                profile.NumCtx,
                profile.ResponseFormat,
                profile.ResearchSourceKey
            );
        }
    }

    private async Task<ProblemDetails?> ValidateResearchSourceAsync(
        SaveTenantAiProfileRequest request,
        CancellationToken ct
    )
    {
        var mode = ResolveWebResearchMode(request);
        if (!request.AllowExternalResearch || mode != TenantAiWebResearchMode.PatchHoundManaged)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.ResearchSourceKey))
        {
            return new ProblemDetails { Title = "PatchHound-managed web research requires a research source." };
        }

        var source = await _dbContext.EnrichmentSourceConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.SourceKey == request.ResearchSourceKey.Trim(),
                ct
            );

        if (source is null)
        {
            source = EnrichmentSourceCatalog.CreateDefaults()
                .FirstOrDefault(item => string.Equals(item.SourceKey, request.ResearchSourceKey.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (source is null || !source.Enabled || !EnrichmentSourceCatalog.HasTarget(source, EnrichmentSourceCatalog.AiResearchTarget))
        {
            return new ProblemDetails { Title = "Selected research source is not available for AI research." };
        }

        return null;
    }

    private static string BuildSecretRef(Guid tenantId, Guid profileId) =>
        $"tenants/{tenantId}/ai/{profileId}";

    private static bool TryParseProviderType(string value, out TenantAiProviderType providerType) =>
        Enum.TryParse(value, true, out providerType);

    private static ProblemDetails? ValidateRequest(
        SaveTenantAiProfileRequest request,
        TenantAiProviderType providerType,
        bool isUpdate
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ProblemDetails { Title = "Profile name is required." };
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return new ProblemDetails { Title = "Model is required." };
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return new ProblemDetails { Title = "System prompt is required." };
        }

        if (request.Temperature < 0m || request.Temperature > 2m)
        {
            return new ProblemDetails { Title = "Temperature must be between 0 and 2." };
        }

        if (request.TopP is < 0m or > 1m)
        {
            return new ProblemDetails { Title = "Top P must be between 0 and 1 when provided." };
        }

        if (request.MaxOutputTokens <= 0)
        {
            return new ProblemDetails { Title = "Max output tokens must be greater than 0." };
        }

        if (request.TimeoutSeconds <= 0 || request.TimeoutSeconds > 3600)
        {
            return new ProblemDetails { Title = "Timeout seconds must be between 1 and 3600." };
        }

        if (request.NumCtx is int ctx && ctx <= 0)
        {
            return new ProblemDetails { Title = "Num ctx must be greater than 0 when provided." };
        }

        if (!TryParseResponseFormat(request.ResponseFormat, out _))
        {
            return new ProblemDetails { Title = "Response format must be 'None' or 'Json'." };
        }

        if (request.IsDefault && !request.IsEnabled)
        {
            return new ProblemDetails { Title = "Default AI profiles must be enabled." };
        }

        if (request.AllowExternalResearch && request.MaxResearchSources <= 0)
        {
            return new ProblemDetails { Title = "Max research sources must be greater than 0 when external research is enabled." };
        }

        if (
            request.AllowExternalResearch
            && Enum.TryParse<TenantAiWebResearchMode>(request.WebResearchMode, true, out var researchMode)
            && researchMode == TenantAiWebResearchMode.ProviderNative
            && providerType != TenantAiProviderType.OpenAi
        )
        {
            return new ProblemDetails { Title = "Provider-native web research is currently supported only for OpenAI profiles." };
        }

        switch (providerType)
        {
            case TenantAiProviderType.Ollama:
                if (string.IsNullOrWhiteSpace(request.BaseUrl))
                {
                    return new ProblemDetails { Title = "Base URL is required for Ollama." };
                }
                break;
            case TenantAiProviderType.AzureOpenAi:
                if (string.IsNullOrWhiteSpace(request.BaseUrl))
                {
                    return new ProblemDetails { Title = "Endpoint is required for Azure OpenAI." };
                }

                if (string.IsNullOrWhiteSpace(request.DeploymentName))
                {
                    return new ProblemDetails { Title = "Deployment name is required for Azure OpenAI." };
                }

                if (string.IsNullOrWhiteSpace(request.ApiVersion))
                {
                    return new ProblemDetails { Title = "API version is required for Azure OpenAI." };
                }

                if (!isUpdate && string.IsNullOrWhiteSpace(request.ApiKey))
                {
                    return new ProblemDetails { Title = "API key is required for Azure OpenAI." };
                }
                break;
            case TenantAiProviderType.OpenAi:
                if (string.IsNullOrWhiteSpace(request.BaseUrl))
                {
                    return new ProblemDetails { Title = "Base URL is required for OpenAI." };
                }

                if (!isUpdate && string.IsNullOrWhiteSpace(request.ApiKey))
                {
                    return new ProblemDetails { Title = "API key is required for OpenAI." };
                }
                break;
        }

        return null;
    }

    private static TenantAiProfileDto MapDto(TenantAiProfile profile) =>
        new(
            profile.Id,
            profile.Name,
            profile.ProviderType.ToString(),
            profile.IsDefault,
            profile.IsEnabled,
            profile.Model,
            profile.SystemPrompt,
            profile.Temperature,
            profile.TopP,
            profile.MaxOutputTokens,
            profile.TimeoutSeconds,
            profile.BaseUrl,
            profile.DeploymentName,
            profile.ApiVersion,
            profile.KeepAlive,
            profile.AllowExternalResearch,
            profile.WebResearchMode.ToString(),
            profile.IncludeCitations,
            profile.MaxResearchSources,
            profile.AllowedDomains,
            profile.ResearchSourceKey,
            !string.IsNullOrWhiteSpace(profile.SecretRef),
            profile.LastValidatedAt,
            profile.LastValidationStatus.ToString(),
            profile.LastValidationError,
            profile.NumCtx,
            profile.ResponseFormat.ToString()
        );

    private static TenantAiResponseFormat ResolveResponseFormat(string? value) =>
        TryParseResponseFormat(value, out var parsed) ? parsed : TenantAiResponseFormat.None;

    private static bool TryParseResponseFormat(string? value, out TenantAiResponseFormat result)
    {
        result = TenantAiResponseFormat.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        // Reject numeric strings ("0", "1", "2") - the public API contract is the named enum values only.
        if (int.TryParse(value, out _))
        {
            return false;
        }

        if (!Enum.TryParse<TenantAiResponseFormat>(value, ignoreCase: true, out var parsed))
        {
            return false;
        }

        if (!Enum.IsDefined(typeof(TenantAiResponseFormat), parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    private static TenantAiWebResearchMode ResolveWebResearchMode(
        SaveTenantAiProfileRequest request
    )
    {
        if (!request.AllowExternalResearch)
        {
            return TenantAiWebResearchMode.Disabled;
        }

        return Enum.TryParse<TenantAiWebResearchMode>(request.WebResearchMode, true, out var mode)
            ? mode
            : TenantAiWebResearchMode.PatchHoundManaged;
    }

    private static string ResolveResearchSourceKey(SaveTenantAiProfileRequest request)
    {
        var mode = ResolveWebResearchMode(request);
        return request.AllowExternalResearch && mode == TenantAiWebResearchMode.PatchHoundManaged
            ? request.ResearchSourceKey.Trim()
            : string.Empty;
    }

    private static TenantAiProfileValidationResultDto MapValidationDto(TenantAiProfile profile) =>
        new(
            profile.Id,
            profile.LastValidationStatus.ToString(),
            profile.LastValidationError,
            profile.LastValidatedAt
        );
}
