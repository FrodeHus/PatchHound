using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.VulnerabilitySources;
using System.Text.Json;

namespace PatchHound.Infrastructure.Services;

public class AdvancedToolExecutionService(
    PatchHoundDbContext dbContext,
    DefenderTenantConfigurationProvider defenderConfigurationProvider,
    DefenderApiClient defenderApiClient,
    TenantAiTextGenerationService aiTextGenerationService
)
{
    private sealed record SoftwareEvidenceMetadata(
        string? Vendor,
        string? Product,
        string? Version
    );

    public sealed record AssetExecutionReportResult(
        IReadOnlyList<DefenderAdvancedQuerySchemaColumn> Schema,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        AiTextGenerationResult? Report,
        string? AiUnavailableMessage
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

    public async Task<Result<(string RenderedQuery, AiTextGenerationResult AiResult)>> TestAiSummaryAsync(
        Guid tenantId,
        string query,
        string? aiPrompt,
        IReadOnlyDictionary<string, string?> sampleParameters,
        CancellationToken ct
    )
    {
        AdvancedToolTemplateRenderer.ValidateAllowedParameters(query);
        var renderedQuery = AdvancedToolTemplateRenderer.Render(query, sampleParameters);
        var queryResult = await RunAgainstDefenderAsync(tenantId, renderedQuery, ct);
        var serializedResults = JsonSerializer.Serialize(
            new
            {
                Query = renderedQuery,
                Schema = queryResult.Schema,
                Results = queryResult.Results.Take(100).ToList(),
                ResultCount = queryResult.Results.Count,
            }
        );

        var request = new AiTextGenerationRequest(
            SystemPrompt:
                "You summarize Microsoft Defender advanced hunting query results for security operators. Be concise, factual, and avoid speculation.",
            UserPrompt: string.IsNullOrWhiteSpace(aiPrompt)
                ? "Summarize what the advanced hunting results show, call out the most important evidence, and explain what the operator should conclude next."
                : aiPrompt.Trim(),
            ExternalContext: serializedResults,
            IncludeCitations: false
        );

        var aiResult = await aiTextGenerationService.GenerateAsync(tenantId, null, request, ct);
        if (!aiResult.IsSuccess)
        {
            return Result<(string RenderedQuery, AiTextGenerationResult AiResult)>.Failure(
                aiResult.Error ?? "Failed to generate AI summary."
            );
        }

        return Result<(string RenderedQuery, AiTextGenerationResult AiResult)>.Success(
            (renderedQuery, aiResult.Value)
        );
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
                    ["deviceId"] = context.DeviceExternalId,
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
                    ["deviceId"] = context.DeviceExternalId,
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

    public async Task<AssetExecutionReportResult> RunForAssetReportAsync(
        Guid tenantId,
        Guid assetId,
        string query,
        string? aiPrompt,
        bool useAllOpenVulnerabilities,
        IReadOnlyList<Guid>? vulnerabilityIds,
        CancellationToken ct
    )
    {
        var queryResults = await RunForAssetAsync(
            tenantId,
            assetId,
            query,
            useAllOpenVulnerabilities,
            vulnerabilityIds,
            ct
        );

        var merged = MergeQueryResults(queryResults);
        var serializedResults = JsonSerializer.Serialize(
            new
            {
                Schema = merged.Schema,
                Results = merged.Rows.Take(200).ToList(),
                RowCount = merged.Rows.Count,
            }
        );

        var request = new AiTextGenerationRequest(
            SystemPrompt:
                "You summarize Microsoft Defender advanced hunting results for security operators. Be concise, factual, and focus on installation evidence, scope, and the most useful next conclusion.",
            UserPrompt: string.IsNullOrWhiteSpace(aiPrompt)
                ? "Summarize what these merged advanced hunting results show about the device, highlight the strongest evidence, and explain the operational conclusion."
                : aiPrompt.Trim(),
            ExternalContext: serializedResults,
            IncludeCitations: false
        );

        var aiResult = await aiTextGenerationService.GenerateAsync(tenantId, null, request, ct);
        return aiResult.IsSuccess
            ? new AssetExecutionReportResult(merged.Schema, merged.Rows, aiResult.Value, null)
            : new AssetExecutionReportResult(merged.Schema, merged.Rows, null, aiResult.Error);
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

        // Phase-2: VulnerabilityAsset + SoftwareVulnerabilityMatch deleted. Return empty context.
        var openVulnerabilityRows = new List<(Guid TenantVulnerabilityId, string ExternalId)>();
        var requestedIds = new HashSet<Guid>();
        var vulnerabilityRows = openVulnerabilityRows;
        var softwareEvidenceRows = new List<(string ExternalId, string? Metadata)>();

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
            asset.ExternalId,
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

    private static (IReadOnlyList<DefenderAdvancedQuerySchemaColumn> Schema, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows) MergeQueryResults(
        IReadOnlyList<(string Label, Guid? VulnerabilityId, string? VulnerabilityExternalId, string Query, DefenderAdvancedQueryResult Result)> queryResults
    )
    {
        var schemaByName = new Dictionary<string, DefenderAdvancedQuerySchemaColumn>(StringComparer.Ordinal);
        schemaByName["Context"] = new DefenderAdvancedQuerySchemaColumn("Context", "String");

        foreach (var queryResult in queryResults)
        {
            if (!string.IsNullOrWhiteSpace(queryResult.VulnerabilityExternalId))
            {
                schemaByName["Vulnerability"] = new DefenderAdvancedQuerySchemaColumn("Vulnerability", "String");
            }

            foreach (var column in queryResult.Result.Schema)
            {
                if (!schemaByName.ContainsKey(column.Name))
                {
                    schemaByName[column.Name] = column;
                }
            }
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var queryResult in queryResults)
        {
            foreach (var row in queryResult.Result.Results)
            {
                var mergedRow = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Context"] = queryResult.Label,
                };

                if (!string.IsNullOrWhiteSpace(queryResult.VulnerabilityExternalId))
                {
                    mergedRow["Vulnerability"] = queryResult.VulnerabilityExternalId;
                }

                foreach (var entry in row)
                {
                    mergedRow[entry.Key] = entry.Value;
                }

                rows.Add(mergedRow);
            }
        }

        return (schemaByName.Values.ToList(), rows);
    }
}
