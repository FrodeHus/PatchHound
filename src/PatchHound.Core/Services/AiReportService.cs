using System.Security.Cryptography;
using System.Text;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Core.Services;

public class AiReportService
{
    private readonly IEnumerable<IAiReportProvider> _providers;
    private readonly ITenantAiConfigurationResolver _configurationResolver;

    public AiReportService(
        IEnumerable<IAiReportProvider> providers,
        ITenantAiConfigurationResolver configurationResolver
    )
    {
        _providers = providers;
        _configurationResolver = configurationResolver;
    }

    public async Task<Result<AIReport>> GenerateReportAsync(
        VulnerabilityDefinition vulnerabilityDefinition,
        Guid tenantVulnerabilityId,
        IReadOnlyList<Asset> affectedAssets,
        Guid tenantId,
        Guid userId,
        Guid? tenantAiProfileId,
        CancellationToken ct
    )
    {
        var resolvedResult = tenantAiProfileId.HasValue
            ? await _configurationResolver.ResolveByIdAsync(tenantId, tenantAiProfileId.Value, ct)
            : await _configurationResolver.ResolveDefaultAsync(tenantId, ct);

        if (!resolvedResult.IsSuccess)
        {
            return Result<AIReport>.Failure(
                resolvedResult.Error ?? "Unable to resolve tenant AI configuration."
            );
        }

        var resolvedProfile = resolvedResult.Value;

        var provider = _providers.FirstOrDefault(p => p.ProviderType == resolvedProfile.Profile.ProviderType);

        if (provider is null)
        {
            return Result<AIReport>.Failure(
                $"No AI provider implementation is registered for {resolvedProfile.Profile.ProviderType}."
            );
        }

        var validationResult = await provider.ValidateAsync(resolvedProfile, ct);
        if (!validationResult.IsSuccess)
        {
            return Result<AIReport>.Failure(validationResult.Error);
        }

        var content = await provider.GenerateReportAsync(
            new AiReportGenerationRequest(vulnerabilityDefinition, affectedAssets),
            resolvedProfile,
            ct
        );

        var report = AIReport.Create(
            tenantVulnerabilityId,
            tenantId,
            resolvedProfile.Profile.Id,
            content,
            resolvedProfile.Profile.ProviderType.ToString(),
            resolvedProfile.Profile.Name,
            resolvedProfile.Profile.Model,
            HashPrompt(resolvedProfile.Profile.SystemPrompt),
            resolvedProfile.Profile.Temperature,
            resolvedProfile.Profile.MaxOutputTokens,
            userId
        );

        return Result<AIReport>.Success(report);
    }

    private static string HashPrompt(string prompt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prompt ?? string.Empty));
        return Convert.ToHexString(bytes);
    }
}
