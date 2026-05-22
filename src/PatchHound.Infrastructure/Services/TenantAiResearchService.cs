using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;
using Microsoft.EntityFrameworkCore;

namespace PatchHound.Infrastructure.Services;

public class TenantAiResearchService(
    LocalVulnerabilityIntelResearchProvider localVulnerabilityIntelProvider,
    IEnumerable<IAiResearchSourceProvider> researchSourceProviders,
    PatchHoundDbContext dbContext
) : ITenantAiResearchService
{
    private const int MaxCombinedContextChars = 12000;

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
                    await ResearchExternalAsync(request, ct),
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
                Truncate(string.Join("\n\n", contexts), MaxCombinedContextChars),
                sources.GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Take(request.MaxSources)
                    .ToList()
            )
        );
    }

    private async Task<Result<AiWebResearchBundle>> ResearchExternalAsync(
        AiWebResearchRequest request,
        CancellationToken ct
    )
    {
        var sourceKey = string.IsNullOrWhiteSpace(request.ResearchSourceKey)
            ? EnrichmentSourceCatalog.ExternalWebSearchSourceKey
            : request.ResearchSourceKey.Trim();

        if (string.Equals(sourceKey, EnrichmentSourceCatalog.ExternalWebSearchSourceKey, StringComparison.OrdinalIgnoreCase))
        {
            var legacyProvider = researchSourceProviders.FirstOrDefault(
                item => string.Equals(item.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase)
            );
            return legacyProvider is null
                ? Result<AiWebResearchBundle>.Failure("Default web research provider is not registered.")
                : await legacyProvider.ResearchAsync(
                    EnrichmentSourceConfiguration.Create(sourceKey, "External web search", true),
                    request,
                    ct
                );
        }

        var source = await dbContext.EnrichmentSourceConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SourceKey == sourceKey, ct);
        source ??= EnrichmentSourceCatalog.CreateDefaults()
            .FirstOrDefault(item => string.Equals(item.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return Result<AiWebResearchBundle>.Failure("Selected research source is not configured.");
        }

        if (!string.Equals(sourceKey, EnrichmentSourceCatalog.ExternalWebSearchSourceKey, StringComparison.OrdinalIgnoreCase)
            && (!source.Enabled || !EnrichmentSourceCatalog.HasTarget(source, EnrichmentSourceCatalog.AiResearchTarget)))
        {
            return Result<AiWebResearchBundle>.Failure("Selected research source is not available for AI research.");
        }

        var provider = researchSourceProviders.FirstOrDefault(
            item => string.Equals(item.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase)
        );

        if (provider is null)
        {
            return Result<AiWebResearchBundle>.Failure("Selected research source has no registered provider.");
        }

        return await provider.ResearchAsync(source, request, ct);
    }

    private static string Truncate(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return value[..FindTruncationBoundary(value, maxChars)].TrimEnd() + "\n[truncated]";
    }

    private static int FindTruncationBoundary(string value, int maxChars)
    {
        var newline = value.LastIndexOf('\n', maxChars - 1, maxChars);
        if (newline > maxChars / 2)
        {
            return newline;
        }

        for (var index = maxChars - 1; index >= maxChars / 2; index--)
        {
            if (value[index] is '.' or '!' or '?')
            {
                return index + 1;
            }
        }

        for (var index = maxChars - 1; index >= maxChars / 2; index--)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return maxChars;
    }
}
