using System.Text;
using Microsoft.Extensions.Options;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Options;

namespace PatchHound.Infrastructure.AiProviders;

public class AnthropicProvider : IAiReportProvider
{
    private readonly AiProviderOptions _options;

    public AnthropicProvider(IOptions<AiProviderOptions> options)
    {
        _options = options.Value;
    }

    public string ProviderName => "Anthropic";

    public Task<string> GenerateReportAsync(
        VulnerabilityDefinition vulnerabilityDefinition,
        IReadOnlyList<Asset> affectedAssets,
        CancellationToken ct
    )
    {
        // Stub implementation - replace with actual Anthropic API call
        var sb = new StringBuilder();
        sb.AppendLine($"# AI Vulnerability Report: {vulnerabilityDefinition.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Provider:** Anthropic ({_options.ModelId ?? "claude-sonnet"})");
        sb.AppendLine($"**Vulnerability:** {vulnerabilityDefinition.ExternalId}");
        sb.AppendLine($"**Severity:** {vulnerabilityDefinition.VendorSeverity}");
        sb.AppendLine($"**CVSS Score:** {vulnerabilityDefinition.CvssScore?.ToString("F1") ?? "N/A"}");
        sb.AppendLine();
        sb.AppendLine("## Executive Summary");
        sb.AppendLine(
            $"This report analyzes {vulnerabilityDefinition.ExternalId} affecting {affectedAssets.Count} asset(s)."
        );
        sb.AppendLine();
        sb.AppendLine("## Technical Analysis");
        sb.AppendLine(vulnerabilityDefinition.Description);
        sb.AppendLine();
        sb.AppendLine("## Impact Assessment");
        foreach (var asset in affectedAssets)
        {
            sb.AppendLine(
                $"- **{asset.Name}** ({asset.AssetType}, Criticality: {asset.Criticality})"
            );
        }
        sb.AppendLine();
        sb.AppendLine("## Remediation Guidance");
        sb.AppendLine("- Prioritize patching based on asset criticality.");
        sb.AppendLine("- Implement network segmentation for exposed assets.");
        sb.AppendLine("- Enable enhanced monitoring and alerting.");
        sb.AppendLine();
        sb.AppendLine(
            "*This is a stub report. Connect to the Anthropic API for real AI-generated analysis.*"
        );

        return Task.FromResult(sb.ToString());
    }
}
