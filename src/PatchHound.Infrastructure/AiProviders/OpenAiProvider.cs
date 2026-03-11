using System.Text;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.AiProviders;

public class OpenAiProvider : IAiReportProvider
{
    public TenantAiProviderType ProviderType => TenantAiProviderType.OpenAi;

    public Task<string> GenerateReportAsync(
        AiReportGenerationRequest request,
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# AI Vulnerability Report: {request.VulnerabilityDefinition.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Provider:** OpenAI ({profile.Profile.Model})");
        sb.AppendLine($"**Vulnerability:** {request.VulnerabilityDefinition.ExternalId}");
        sb.AppendLine($"**Severity:** {request.VulnerabilityDefinition.VendorSeverity}");
        sb.AppendLine($"**CVSS Score:** {request.VulnerabilityDefinition.CvssScore?.ToString("F1") ?? "N/A"}");
        sb.AppendLine();
        sb.AppendLine("## Executive Summary");
        sb.AppendLine(
            $"This report analyzes {request.VulnerabilityDefinition.ExternalId} affecting {request.AffectedAssets.Count} asset(s)."
        );
        sb.AppendLine();
        sb.AppendLine("## Technical Analysis");
        sb.AppendLine(request.VulnerabilityDefinition.Description);
        sb.AppendLine();
        sb.AppendLine("## Recommendations");
        sb.AppendLine("- Prioritize remediation by asset criticality and exposure.");
        sb.AppendLine("- Apply vendor patches or compensating controls promptly.");
        sb.AppendLine("- Reassess exposure after mitigation.");
        sb.AppendLine();
        sb.AppendLine("*This is a stub report. Connect to OpenAI for real AI-generated analysis.*");

        return Task.FromResult(sb.ToString());
    }

    public Task<AiProviderValidationResult> ValidateAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(profile.Profile.Model))
        {
            return Task.FromResult(
                AiProviderValidationResult.Failure("Model is required for OpenAI.")
            );
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return Task.FromResult(
                AiProviderValidationResult.Failure("API key is required for OpenAI.")
            );
        }

        return Task.FromResult(AiProviderValidationResult.Success());
    }
}
