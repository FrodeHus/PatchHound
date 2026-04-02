using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.VulnerabilitySources;
using System.Text.Json;

namespace PatchHound.Infrastructure.Services;

public class AdvancedToolExecutionService(
    PatchHoundDbContext dbContext,
    DefenderTenantConfigurationProvider defenderConfigurationProvider,
    DefenderApiClient defenderApiClient
)
{
    private sealed record SoftwareEvidenceMetadata(
        string? Vendor,
        string? Product,
        string? Version
    );

    public async Task<DefenderAdvancedQueryResult> TestQueryAsync(
        Guid tenantId,
        string query,
        IReadOnlyDictionary<string, string?> sampleParameters,
        CancellationToken ct
    )
    {
        AdvancedToolTemplateRenderer.ValidateAllowedParameters(query);
        var renderedQuery = AdvancedToolTemplateRenderer.Render(query, sampleParameters);
        return await RunAgainstDefenderAsync(tenantId, renderedQuery, ct);
    }

    public async Task<IReadOnlyList<(string Label, Guid? VulnerabilityId, string? VulnerabilityExternalId, string Query, DefenderAdvancedQueryResult Result)>> RunForAssetAsync(
        Guid tenantId,
        Guid assetId,
        string query,
        bool useAllOpenVulnerabilities,
        IReadOnlyList<Guid>? vulnerabilityIds,
        CancellationToken ct
    )
    {
        AdvancedToolTemplateRenderer.ValidateAllowedParameters(query);
        var context = await BuildAssetExecutionContextAsync(
            tenantId,
            assetId,
            useAllOpenVulnerabilities,
            vulnerabilityIds,
            ct
        );

        if (!string.Equals(context.AssetType, "Device", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Advanced tools currently support only device assets.");
        }

        var placeholders = AdvancedToolTemplateRenderer.ExtractPlaceholders(query);
        var requiresVulnerabilityContext = placeholders.Any(value => value.StartsWith("vuln.", StringComparison.Ordinal));

        if (!requiresVulnerabilityContext)
        {
            var renderedQuery = AdvancedToolTemplateRenderer.Render(
                query,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["deviceName"] = context.DeviceName,
                }
            );
            var result = await RunAgainstDefenderAsync(tenantId, renderedQuery, ct);
            return
            [
                ("Asset context", null, null, renderedQuery, result),
            ];
        }

        var selectedVulnerabilities = context.Vulnerabilities;
        if (selectedVulnerabilities.Count == 0)
        {
            throw new InvalidOperationException(
                "This tool requires vulnerability context, but no open vulnerabilities were selected."
            );
        }

        var responses = new List<(string, Guid?, string?, string, DefenderAdvancedQueryResult)>(
            selectedVulnerabilities.Count
        );
        foreach (var vulnerability in selectedVulnerabilities)
        {
            var renderedQuery = AdvancedToolTemplateRenderer.Render(
                query,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["deviceName"] = context.DeviceName,
                    ["vuln.name"] = vulnerability.ExternalId,
                    ["vuln.vendor"] = vulnerability.Vendor,
                    ["vuln.product"] = vulnerability.Product,
                    ["vuln.version"] = vulnerability.Version,
                }
            );
            var result = await RunAgainstDefenderAsync(tenantId, renderedQuery, ct);
            responses.Add((vulnerability.ExternalId, vulnerability.VulnerabilityId, vulnerability.ExternalId, renderedQuery, result));
        }

        return responses;
    }

    private async Task<DefenderAdvancedQueryResult> RunAgainstDefenderAsync(
        Guid tenantId,
        string renderedQuery,
        CancellationToken ct
    )
    {
        var configuration = await defenderConfigurationProvider.GetConfigurationAsync(tenantId, ct);
        if (configuration is null)
        {
            throw new InvalidOperationException("Microsoft Defender is not configured for the active tenant.");
        }

        return await defenderApiClient.RunAdvancedQueryAsync(configuration, renderedQuery, ct);
    }

    private async Task<AdvancedToolExecutionContext> BuildAssetExecutionContextAsync(
        Guid tenantId,
        Guid assetId,
        bool useAllOpenVulnerabilities,
        IReadOnlyList<Guid>? vulnerabilityIds,
        CancellationToken ct
    )
    {
        var asset = await dbContext.Assets.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == assetId && item.TenantId == tenantId, ct);
        if (asset is null)
        {
            throw new InvalidOperationException("Asset was not found.");
        }

        if (asset.AssetType != Core.Enums.AssetType.Device)
        {
            throw new InvalidOperationException(
                "Advanced tools are only available for Defender-backed device assets."
            );
        }

        var openVulnerabilityRows = await dbContext.VulnerabilityAssets.AsNoTracking()
            .Where(item => item.AssetId == assetId && item.ResolvedDate == null)
            .Select(item => new
            {
                item.TenantVulnerabilityId,
                ExternalId = item.TenantVulnerability.VulnerabilityDefinition.ExternalId,
            })
            .ToListAsync(ct);

        var requestedIds = useAllOpenVulnerabilities
            ? openVulnerabilityRows.Select(item => item.TenantVulnerabilityId).ToHashSet()
            : (vulnerabilityIds ?? []).ToHashSet();

        var vulnerabilityRows = requestedIds.Count == 0
            ? []
            : await dbContext.VulnerabilityAssets.AsNoTracking()
                .Where(item =>
                    item.AssetId == assetId
                    && item.ResolvedDate == null
                    && requestedIds.Contains(item.TenantVulnerabilityId)
                )
                .Select(item => new
                {
                    item.TenantVulnerabilityId,
                    ExternalId = item.TenantVulnerability.VulnerabilityDefinition.ExternalId,
                })
                .ToListAsync(ct);

        var softwareEvidenceRows = vulnerabilityRows.Count == 0
            ? []
            : await dbContext.DeviceSoftwareInstallations.AsNoTracking()
                .Where(item => item.DeviceAssetId == assetId)
                .Join(
                    dbContext.SoftwareVulnerabilityMatches.AsNoTracking(),
                    installation => installation.SoftwareAssetId,
                    match => match.SoftwareAssetId,
                    (installation, match) => new
                    {
                        match.SoftwareAssetId,
                        match.VulnerabilityDefinition.ExternalId,
                    }
                )
                .Join(
                    dbContext.Assets.AsNoTracking(),
                    row => row.SoftwareAssetId,
                    software => software.Id,
                    (row, software) => new
                    {
                        row.ExternalId,
                        software.Metadata,
                    }
                )
                .ToListAsync(ct);

        var evidenceByExternalId = softwareEvidenceRows
            .GroupBy(row => row.ExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ExtractSoftwareMetadata(group.Select(item => item.Metadata).FirstOrDefault()),
                StringComparer.OrdinalIgnoreCase
            );

        var vulnerabilities = vulnerabilityRows
            .Select(row =>
            {
                evidenceByExternalId.TryGetValue(row.ExternalId, out var softwareEvidence);
                return new AdvancedToolVulnerabilityContext(
                    row.TenantVulnerabilityId,
                    row.ExternalId,
                    softwareEvidence?.Vendor,
                    softwareEvidence?.Product,
                    softwareEvidence?.Version
                );
            })
            .ToList();

        var deviceName = asset.DeviceComputerDnsName ?? asset.Name;
        return new AdvancedToolExecutionContext(
            tenantId,
            assetId,
            asset.AssetType.ToString(),
            deviceName,
            vulnerabilities
        );
    }

    private static SoftwareEvidenceMetadata? ExtractSoftwareMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            return new SoftwareEvidenceMetadata(
                root.TryGetProperty("vendor", out var vendor) ? vendor.GetString() : null,
                root.TryGetProperty("name", out var product) ? product.GetString() : null,
                root.TryGetProperty("version", out var version) ? version.GetString() : null
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
