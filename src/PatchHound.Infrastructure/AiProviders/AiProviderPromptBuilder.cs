using System.Text;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.AiProviders;

internal static class AiProviderPromptBuilder
{
    public static string BuildReportPrompt(AiReportGenerationRequest request)
    {
        var vulnerability = request.VulnerabilityDefinition;
        var builder = new StringBuilder();

        builder.AppendLine("Generate a vulnerability report for the following tenant context.");
        builder.AppendLine();
        builder.AppendLine($"Vulnerability ID: {vulnerability.ExternalId}");
        builder.AppendLine($"Title: {vulnerability.Title}");
        builder.AppendLine($"Severity: {vulnerability.VendorSeverity}");
        builder.AppendLine($"CVSS Score: {vulnerability.CvssScore?.ToString("F1") ?? "N/A"}");
        builder.AppendLine($"Source: {vulnerability.Source}");
        builder.AppendLine($"Published: {vulnerability.PublishedDate?.ToString("O") ?? "Unknown"}");
        builder.AppendLine();
        builder.AppendLine("Description:");
        builder.AppendLine(vulnerability.Description);
        builder.AppendLine();
        builder.AppendLine($"Affected assets in tenant: {request.AffectedAssets.Count}");

        foreach (var asset in request.AffectedAssets.Take(50))
        {
            builder.AppendLine(
                $"- {asset.Name} | Type: {asset.AssetType} | Criticality: {asset.Criticality}"
            );
        }

        if (request.AffectedAssets.Count > 50)
        {
            builder.AppendLine($"- ...and {request.AffectedAssets.Count - 50} more assets");
        }

        builder.AppendLine();
        builder.AppendLine("Respond in markdown using the configured report style.");

        return builder.ToString();
    }

    public static string BuildValidationPrompt() => "Respond with exactly OK.";
}
