using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class SupplyChainEvidenceEnrichmentRunner(
    IServiceScopeFactory scopeFactory,
    SupplyChainCatalogClient client,
    ILogger<SupplyChainEvidenceEnrichmentRunner> logger
) : IEnrichmentSourceRunner
{
    public string SourceKey => EnrichmentSourceCatalog.SupplyChainSourceKey;
    public EnrichmentTargetModel TargetModel => EnrichmentTargetModel.SoftwareAsset;
    public TimeSpan MinimumDelay => TimeSpan.FromMilliseconds(500);

    public async Task<EnrichmentJobExecutionResult> ExecuteAsync(
        EnrichmentJob job,
        CancellationToken ct
    )
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var importService = scope.ServiceProvider.GetRequiredService<CycloneDxSupplyChainImportService>();

        if (job.TargetModel != EnrichmentTargetModel.SoftwareAsset)
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Failed,
                $"Unsupported target model {job.TargetModel}."
            );
        }

        var software = await dbContext
            .SoftwareProducts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == job.TargetId, ct);
        if (software is null)
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.NoData,
                "SoftwareProduct no longer exists."
            );
        }

        var sourceConfig = await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                source =>
                    source.SourceKey == EnrichmentSourceCatalog.SupplyChainSourceKey && source.Enabled,
                ct
            );
        if (sourceConfig is null || string.IsNullOrWhiteSpace(sourceConfig.ApiBaseUrl))
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                "Supply-chain source configuration unavailable.",
                DateTimeOffset.UtcNow.AddMinutes(5)
            );
        }

        try
        {
            var documentUrl = await ResolveDocumentUrlAsync(sourceConfig.ApiBaseUrl, software, ct);
            if (string.IsNullOrWhiteSpace(documentUrl))
            {
                return new EnrichmentJobExecutionResult(
                    EnrichmentJobExecutionOutcome.NoData,
                    "No matching SBOM/VEX document was found for this product."
                );
            }

            var documentJson = await client.GetStringAsync(documentUrl, ct);
            await importService.ImportAsyncForNormalizedSoftware(software.Id, documentJson, ct);

            logger.LogInformation(
                "Supply-chain evidence applied to SoftwareProduct {SoftwareId} from {DocumentUrl}.",
                software.Id,
                documentUrl
            );

            return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.Succeeded);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.NoData, ex.Message);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                "Rate-limited while fetching supply-chain evidence.",
                DateTimeOffset.UtcNow.AddMinutes(1)
            );
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HTTP error enriching SoftwareProduct {SoftwareId}.", software.Id);
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                $"HTTP error: {ex.Message}",
                DateTimeOffset.UtcNow.AddMinutes(5)
            );
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                "Supply-chain evidence request timed out.",
                DateTimeOffset.UtcNow.AddMinutes(5)
            );
        }
        catch (InvalidOperationException ex)
        {
            return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.NoData, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new EnrichmentJobExecutionResult(
                EnrichmentJobExecutionOutcome.Retry,
                "Concurrency conflict.",
                DateTimeOffset.UtcNow.AddMinutes(1)
            );
        }
    }

    private async Task<string?> ResolveDocumentUrlAsync(
        string catalogUrlOrTemplate,
        SoftwareProduct software,
        CancellationToken ct
    )
    {
        if (catalogUrlOrTemplate.Contains('{'))
        {
            return ExpandTemplate(catalogUrlOrTemplate, software);
        }

        var catalogJson = await client.GetStringAsync(catalogUrlOrTemplate, ct);
        using var document = JsonDocument.Parse(catalogJson);
        var root = document.RootElement;

        IEnumerable<JsonElement> entries = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray(),
            JsonValueKind.Object when root.TryGetProperty("documents", out var documents)
                && documents.ValueKind == JsonValueKind.Array => documents.EnumerateArray(),
            _ => [],
        };

        var canonicalProductKey = software.CanonicalProductKey.Trim();
        var canonicalName = software.Name.Trim();
        var canonicalVendor = software.Vendor?.Trim();

        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!IsMatch(entry, canonicalProductKey, canonicalName, canonicalVendor))
            {
                continue;
            }

            var url =
                entry.GetPropertyOrDefault("documentUrl")
                ?? entry.GetPropertyOrDefault("url")
                ?? entry.GetPropertyOrDefault("sbomUrl")
                ?? entry.GetPropertyOrDefault("vexUrl");
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    private static bool IsMatch(
        JsonElement entry,
        string canonicalProductKey,
        string canonicalName,
        string? canonicalVendor
    )
    {
        var productKey = entry.GetPropertyOrDefault("productKey");
        if (string.Equals(productKey, canonicalProductKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var vendor = entry.GetPropertyOrDefault("vendor");
        var name = entry.GetPropertyOrDefault("name");
        if (
            !string.IsNullOrWhiteSpace(name)
            && string.Equals(name, canonicalName, StringComparison.OrdinalIgnoreCase)
            && (
                string.IsNullOrWhiteSpace(vendor)
                || string.IsNullOrWhiteSpace(canonicalVendor)
                || string.Equals(vendor, canonicalVendor, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return true;
        }

        if (entry.TryGetProperty("productKeys", out var productKeys) && productKeys.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in productKeys.EnumerateArray())
            {
                if (
                    value.ValueKind == JsonValueKind.String
                    && string.Equals(value.GetString(), canonicalProductKey, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ExpandTemplate(string template, SoftwareProduct software)
    {
        return template
            .Replace("{productKey}", Uri.EscapeDataString(software.CanonicalProductKey), StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", Uri.EscapeDataString(software.Name), StringComparison.OrdinalIgnoreCase)
            .Replace("{vendor}", Uri.EscapeDataString(software.Vendor ?? string.Empty), StringComparison.OrdinalIgnoreCase);
    }
}
