using System.Text;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.AiProviders;

public class OllamaAiProvider : IAiReportProvider
{
    public TenantAiProviderType ProviderType => TenantAiProviderType.Ollama;

    public Task<string> GenerateReportAsync(
        AiReportGenerationRequest request,
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# AI Vulnerability Report: {request.VulnerabilityDefinition.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Provider:** Ollama ({profile.Profile.Model})");
        sb.AppendLine($"**Vulnerability:** {request.VulnerabilityDefinition.ExternalId}");
        sb.AppendLine($"**Severity:** {request.VulnerabilityDefinition.VendorSeverity}");
        sb.AppendLine($"**CVSS Score:** {request.VulnerabilityDefinition.CvssScore?.ToString("F1") ?? "N/A"}");
        sb.AppendLine();
        sb.AppendLine("## Local Analysis Summary");
        sb.AppendLine(
            $"This report analyzes {request.VulnerabilityDefinition.ExternalId} affecting {request.AffectedAssets.Count} asset(s) using the tenant's Ollama profile."
        );
        sb.AppendLine();
        sb.AppendLine("## Technical Analysis");
        sb.AppendLine(request.VulnerabilityDefinition.Description);
        sb.AppendLine();
        sb.AppendLine("## Affected Assets");
        foreach (var asset in request.AffectedAssets)
        {
            sb.AppendLine($"- **{asset.Name}** ({asset.AssetType}, Criticality: {asset.Criticality})");
        }
        sb.AppendLine();
        sb.AppendLine("*This is a stub report. Connect to Ollama for real AI-generated analysis.*");

        return Task.FromResult(sb.ToString());
    }

    public Task<AiProviderValidationResult> ValidateAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(profile.Profile.BaseUrl))
        {
            return Task.FromResult(
                AiProviderValidationResult.Failure("Base URL is required for Ollama.")
            );
        }

        if (string.IsNullOrWhiteSpace(profile.Profile.Model))
        {
            return Task.FromResult(
                AiProviderValidationResult.Failure("Model is required for Ollama.")
            );
        }

        return Task.FromResult(AiProviderValidationResult.Success());
    }
}
