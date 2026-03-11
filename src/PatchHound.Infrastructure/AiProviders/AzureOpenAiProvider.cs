using System.Text;
using PatchHound.Core.Enums;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.AiProviders;

public class AzureOpenAiProvider : IAiReportProvider
{
    public TenantAiProviderType ProviderType => TenantAiProviderType.AzureOpenAi;

    public Task<string> GenerateReportAsync(
        AiReportGenerationRequest request,
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# AI Vulnerability Report: {request.VulnerabilityDefinition.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Provider:** Azure OpenAI ({profile.Profile.Model})");
        sb.AppendLine($"**Vulnerability:** {request.VulnerabilityDefinition.ExternalId}");
        sb.AppendLine($"**Severity:** {request.VulnerabilityDefinition.VendorSeverity}");
        sb.AppendLine($"**CVSS Score:** {request.VulnerabilityDefinition.CvssScore?.ToString("F1") ?? "N/A"}");
        sb.AppendLine();
        sb.AppendLine("## Description");
        sb.AppendLine(request.VulnerabilityDefinition.Description);
        sb.AppendLine();
        sb.AppendLine("## Affected Assets");
        foreach (var asset in request.AffectedAssets)
        {
            sb.AppendLine(
                $"- **{asset.Name}** ({asset.AssetType}, Criticality: {asset.Criticality})"
            );
        }
        sb.AppendLine();
        sb.AppendLine("## Recommendations");
        sb.AppendLine("- Review and apply vendor patches as soon as possible.");
        sb.AppendLine("- Assess compensating controls for high-criticality assets.");
        sb.AppendLine("- Monitor affected assets for indicators of compromise.");
        sb.AppendLine();
        sb.AppendLine(
            "*This is a stub report. Connect to Azure OpenAI for real AI-generated analysis.*"
        );

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
                AiProviderValidationResult.Failure("Endpoint is required for Azure OpenAI.")
            );
        }

        if (string.IsNullOrWhiteSpace(profile.Profile.DeploymentName))
        {
            return Task.FromResult(
                AiProviderValidationResult.Failure("Deployment name is required for Azure OpenAI.")
            );
        }

        if (string.IsNullOrWhiteSpace(profile.Profile.ApiVersion))
        {
            return Task.FromResult(
                AiProviderValidationResult.Failure("API version is required for Azure OpenAI.")
            );
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return Task.FromResult(
                AiProviderValidationResult.Failure("API key is required for Azure OpenAI.")
            );
        }

        return Task.FromResult(AiProviderValidationResult.Success());
    }
}
