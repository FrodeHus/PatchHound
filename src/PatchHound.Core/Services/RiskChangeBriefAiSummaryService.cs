using System.Text.Json;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Core.Services;

public class RiskChangeBriefAiSummaryService : IRiskChangeBriefAiSummaryService
{
    private readonly ITenantAiConfigurationResolver _configurationResolver;
    private readonly ITenantAiResearchService _researchService;
    private readonly TenantAiTextGenerationService _textGenerationService;

    public RiskChangeBriefAiSummaryService(
        ITenantAiConfigurationResolver configurationResolver,
        ITenantAiResearchService researchService,
        TenantAiTextGenerationService textGenerationService
    )
    {
        _configurationResolver = configurationResolver;
        _researchService = researchService;
        _textGenerationService = textGenerationService;
    }

    public async Task<string?> GenerateAsync(
        Guid tenantId,
        RiskChangeBriefSummaryInput brief,
        CancellationToken ct
    )
    {
        if (brief.AppearedCount == 0 && brief.ResolvedCount == 0)
        {
            return null;
        }

        var resolvedProfileResult = await _configurationResolver.ResolveDefaultAsync(tenantId, ct);
        if (!resolvedProfileResult.IsSuccess)
        {
            return null;
        }

        var resolvedProfile = resolvedProfileResult.Value;
        var profile = resolvedProfile.Profile;
        if (!profile.AllowExternalResearch || profile.WebResearchMode == Enums.TenantAiWebResearchMode.Disabled)
        {
            return null;
        }

        var request = new AiTextGenerationRequest(
            "You are a PatchHound risk-change analyst. Write exactly one concise sentence summarizing the most important high or critical vulnerability changes in the last 24 hours. Focus on what appeared, what resolved, and any notable product or exploitation context. Do not use bullets, headings, or filler.",
            JsonSerializer.Serialize(
                new
                {
                    timeWindow = "last 24 hours",
                    appearedCount = brief.AppearedCount,
                    resolvedCount = brief.ResolvedCount,
                    appeared = brief.Appeared.Select(item => new
                    {
                        item.ExternalId,
                        item.Title,
                        item.Severity,
                        item.AffectedAssetCount,
                        item.ChangedAt,
                    }),
                    resolved = brief.Resolved.Select(item => new
                    {
                        item.ExternalId,
                        item.Title,
                        item.Severity,
                        item.AffectedAssetCount,
                        item.ChangedAt,
                    }),
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )
        );

        if (profile.WebResearchMode == Enums.TenantAiWebResearchMode.ProviderNative
            && profile.ProviderType == Enums.TenantAiProviderType.OpenAi)
        {
            request = request with
            {
                UseProviderNativeWebResearch = true,
                AllowedDomains = ParseAllowedDomains(profile.AllowedDomains),
                MaxResearchSources = profile.MaxResearchSources,
                IncludeCitations = profile.IncludeCitations,
            };
        }
        else if (profile.WebResearchMode == Enums.TenantAiWebResearchMode.PatchHoundManaged)
        {
            var researchResult = await _researchService.ResearchAsync(
                resolvedProfile,
                new AiWebResearchRequest(
                    BuildResearchQuery(brief),
                    ParseAllowedDomains(profile.AllowedDomains),
                    profile.MaxResearchSources,
                    profile.IncludeCitations
                ),
                ct
            );

            if (!researchResult.IsSuccess || string.IsNullOrWhiteSpace(researchResult.Value.Context))
            {
                return null;
            }

            request = request with { ExternalContext = researchResult.Value.Context };
        }

        var generationResult = await _textGenerationService.GenerateAsync(
            tenantId,
            profile.Id,
            request,
            ct
        );

        return generationResult.IsSuccess ? generationResult.Value.Content.Trim() : null;
    }

    private static string BuildResearchQuery(RiskChangeBriefSummaryInput brief)
    {
        var identifiers = brief.Appeared
            .Concat(brief.Resolved)
            .Select(item => item.ExternalId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6);

        return $"Recent public context for these vulnerabilities: {string.Join(", ", identifiers)}";
    }

    private static IReadOnlyList<string> ParseAllowedDomains(string? allowedDomains)
    {
        return (allowedDomains ?? string.Empty)
            .Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
