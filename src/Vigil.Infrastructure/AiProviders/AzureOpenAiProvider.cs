using System.Text;
using Microsoft.Extensions.Options;
using Vigil.Core.Entities;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Options;

namespace Vigil.Infrastructure.AiProviders;

public class AzureOpenAiProvider : IAiReportProvider
{
    private readonly AiProviderOptions _options;

    public AzureOpenAiProvider(IOptions<AiProviderOptions> options)
    {
        _options = options.Value;
    }

    public string ProviderName => "AzureOpenAI";

    public Task<string> GenerateReportAsync(
        Vulnerability vulnerability,
        IReadOnlyList<Asset> affectedAssets,
        CancellationToken ct
    )
    {
        // Stub implementation - replace with actual Azure OpenAI API call
        var sb = new StringBuilder();
        sb.AppendLine($"# AI Vulnerability Report: {vulnerability.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Provider:** Azure OpenAI ({_options.ModelId ?? "gpt-4o"})");
        sb.AppendLine($"**Vulnerability:** {vulnerability.ExternalId}");
        sb.AppendLine($"**Severity:** {vulnerability.VendorSeverity}");
        sb.AppendLine($"**CVSS Score:** {vulnerability.CvssScore?.ToString("F1") ?? "N/A"}");
        sb.AppendLine();
        sb.AppendLine("## Description");
        sb.AppendLine(vulnerability.Description);
        sb.AppendLine();
        sb.AppendLine("## Affected Assets");
        foreach (var asset in affectedAssets)
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
}
