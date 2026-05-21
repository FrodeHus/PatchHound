using PatchHound.Core.Common;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services;

public class TenantAiResearchService(
    LocalVulnerabilityIntelResearchProvider localVulnerabilityIntelProvider,
    ExternalWebSearchResearchProvider externalWebSearchProvider
) : ITenantAiResearchService
{
    public async Task<Result<AiWebResearchBundle>> ResearchAsync(
        TenantAiProfileResolved profile,
        AiWebResearchRequest request,
        CancellationToken ct
    )
    {
        var providers = request.Providers is { Count: > 0 }
            ? request.Providers
            : [AiResearchProviderKind.ExternalWebSearch];

        var contexts = new List<string>();
        var sources = new List<AiWebResearchSource>();
        var errors = new List<string>();

        foreach (var provider in providers.Distinct())
        {
            var result = provider switch
            {
                AiResearchProviderKind.LocalVulnerabilityIntel =>
                    await localVulnerabilityIntelProvider.ResearchAsync(request, ct),
                AiResearchProviderKind.ExternalWebSearch =>
                    await externalWebSearchProvider.ResearchAsync(request, ct),
                _ => Result<AiWebResearchBundle>.Success(new AiWebResearchBundle(string.Empty, [])),
            };

            if (!result.IsSuccess)
            {
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    errors.Add(result.Error);
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(result.Value.Context))
            {
                contexts.Add(result.Value.Context);
            }

            sources.AddRange(result.Value.Sources);
        }

        if (contexts.Count == 0 && sources.Count == 0 && errors.Count > 0)
        {
            return Result<AiWebResearchBundle>.Failure(errors[0]);
        }

        return Result<AiWebResearchBundle>.Success(
            new AiWebResearchBundle(
                string.Join("\n\n", contexts),
                sources.GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Take(request.MaxSources)
                    .ToList()
            )
        );
    }
}
