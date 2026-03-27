using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class EndOfLifeSoftwareEnrichmentRunner(
    IServiceScopeFactory scopeFactory,
    EndOfLifeApiClient apiClient,
    ILogger<EndOfLifeSoftwareEnrichmentRunner> logger
) : IEnrichmentSourceRunner
{
    public string SourceKey => EnrichmentSourceCatalog.EndOfLifeSourceKey;
    public EnrichmentTargetModel TargetModel => EnrichmentTargetModel.SoftwareAsset;
    public TimeSpan MinimumDelay => TimeSpan.FromMilliseconds(500);

    public async Task<EnrichmentJobExecutionResult> ExecuteAsync(
        EnrichmentJob job,
        CancellationToken ct
    )
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        if (job.TargetModel != EnrichmentTargetModel.SoftwareAsset)
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Failed,
                $"Unsupported target model {job.TargetModel}."
            );
        }

        var software = await dbContext
            .NormalizedSoftware.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == job.TargetId, ct);

        if (software is null)
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.NoData,
                "NormalizedSoftware no longer exists."
            );
        }

        var sourceConfig = await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                source =>
                    source.SourceKey == EnrichmentSourceCatalog.EndOfLifeSourceKey && source.Enabled,
                ct
            );

        if (sourceConfig is null)
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                "End-of-life source configuration unavailable.",
                DateTimeOffset.UtcNow.AddMinutes(5)
            );
        }

        var apiBaseUrl = string.IsNullOrWhiteSpace(sourceConfig.ApiBaseUrl)
            ? EnrichmentSourceCatalog.DefaultEndOfLifeApiBaseUrl
            : sourceConfig.ApiBaseUrl;

        // Use stored slug or fall back to the product name from the canonical key (vendor|product)
        var productSlug = !string.IsNullOrWhiteSpace(software.EolProductSlug)
            ? software.EolProductSlug
            : ExtractProductName(job.ExternalKey);

        try
        {
            var response = await apiClient.GetProductAsync(apiBaseUrl, productSlug, ct);
            if (response?.Result is null || response.Result.Releases.Count == 0)
            {
                logger.LogInformation(
                    "No end-of-life data found for product slug '{ProductSlug}' (NormalizedSoftware {SoftwareId}).",
                    productSlug,
                    software.Id
                );
                return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.NoData);
            }

            var product = response.Result;

            // Pick the latest maintained release, or fall back to the first release
            var release = product.Releases.FirstOrDefault(r => r.IsMaintained)
                ?? product.Releases[0];

            var now = DateTimeOffset.UtcNow;
            software.UpdateEndOfLife(
                productSlug: productSlug,
                eolDate: TryParseDate(release.EolFrom),
                latestVersion: release.Latest?.Name,
                isLts: release.IsLts,
                supportEndDate: TryParseDate(release.EoasFrom),
                isDiscontinued: release.IsEol && !release.IsMaintained,
                enrichedAt: now
            );

            await dbContext.SaveChangesAsync(ct);

            logger.LogInformation(
                "Enriched NormalizedSoftware {SoftwareId} with end-of-life data from '{ProductSlug}'. EOL={EolDate}, Latest={LatestVersion}, LTS={IsLts}.",
                software.Id,
                productSlug,
                release.EolFrom,
                release.Latest?.Name,
                release.IsLts
            );

            return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.Succeeded);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning(
                "Rate-limited by endoflife.date for slug '{ProductSlug}'. Retrying in 1 minute.",
                productSlug
            );
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                "Rate-limited by endoflife.date API.",
                DateTimeOffset.UtcNow.AddMinutes(1)
            );
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "HTTP error enriching NormalizedSoftware {SoftwareId} from endoflife.date.",
                software.Id
            );
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                $"HTTP error: {ex.Message}",
                DateTimeOffset.UtcNow.AddMinutes(5)
            );
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Timeout enriching NormalizedSoftware {SoftwareId} from endoflife.date.",
                software.Id
            );
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                "Request timed out.",
                DateTimeOffset.UtcNow.AddMinutes(5)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning(
                "Concurrency conflict updating NormalizedSoftware {SoftwareId}. Retrying.",
                software.Id
            );
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                "Concurrency conflict.",
                DateTimeOffset.UtcNow.AddMinutes(1)
            );
        }
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var result) ? result : null;
    }

    private static string ExtractProductName(string canonicalProductKey)
    {
        var separatorIndex = canonicalProductKey.IndexOf('|');
        return separatorIndex >= 0
            ? canonicalProductKey[(separatorIndex + 1)..]
            : canonicalProductKey;
    }
}
