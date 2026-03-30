using PatchHound.Core.Common;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Core.Services;

public class TenantAiTextGenerationService
{
    private readonly IEnumerable<IAiReportProvider> _providers;
    private readonly ITenantAiConfigurationResolver _configurationResolver;

    public TenantAiTextGenerationService(
        IEnumerable<IAiReportProvider> providers,
        ITenantAiConfigurationResolver configurationResolver
    )
    {
        _providers = providers;
        _configurationResolver = configurationResolver;
    }

    public async Task<Result<AiTextGenerationResult>> GenerateAsync(
        Guid tenantId,
        Guid? tenantAiProfileId,
        AiTextGenerationRequest request,
        CancellationToken ct
    )
    {
        var resolvedResult = tenantAiProfileId.HasValue
            ? await _configurationResolver.ResolveByIdAsync(tenantId, tenantAiProfileId.Value, ct)
            : await _configurationResolver.ResolveDefaultAsync(tenantId, ct);

        if (!resolvedResult.IsSuccess)
        {
            return Result<AiTextGenerationResult>.Failure(
                resolvedResult.Error ?? "Unable to resolve tenant AI configuration."
            );
        }

        return await GenerateResolvedAsync(resolvedResult.Value, request, ct);
    }

    public async Task<Result<AiTextGenerationResult>> GenerateResolvedAsync(
        TenantAiProfileResolved resolvedProfile,
        AiTextGenerationRequest request,
        CancellationToken ct
    )
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderType == resolvedProfile.Profile.ProviderType);
        if (provider is null)
        {
            return Result<AiTextGenerationResult>.Failure(
                $"No AI provider implementation is registered for {resolvedProfile.Profile.ProviderType}."
            );
        }

        string content;
        try
        {
            content = await provider.GenerateTextAsync(request, resolvedProfile, ct);
        }
        catch (Exception ex)
        {
            return Result<AiTextGenerationResult>.Failure($"AI generation failed: {ex.Message}");
        }

        return Result<AiTextGenerationResult>.Success(
            new AiTextGenerationResult(
                resolvedProfile.Profile.Id,
                content,
                resolvedProfile.Profile.ProviderType.ToString(),
                resolvedProfile.Profile.Name,
                resolvedProfile.Profile.Model,
                DateTimeOffset.UtcNow
            )
        );
    }
}
