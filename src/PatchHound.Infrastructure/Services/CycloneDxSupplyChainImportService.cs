using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class CycloneDxSupplyChainImportService(PatchHoundDbContext dbContext)
{
    private static readonly Regex VersionRegex = new(
        @"(?:upgrade|update|fixed|fix(?:ed)?\s+in)\s+(?:to\s+)?(?:version\s+)?(?<version>[A-Za-z0-9][A-Za-z0-9._\-+]*)|version\s+(?<version>[A-Za-z0-9][A-Za-z0-9._\-+]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public async Task<SupplyChainImportResult> ImportAsync(
        Guid tenantSoftwareId,
        string documentJson,
        CancellationToken ct
    )
    {
        var tenantSoftware = await dbContext
            .TenantSoftware.Include(item => item.SoftwareProduct)
            .FirstOrDefaultAsync(item => item.Id == tenantSoftwareId, ct);

        if (tenantSoftware is null)
        {
            throw new InvalidOperationException("Tenant software was not found.");
        }

        var parsed = Parse(documentJson);
        tenantSoftware.SoftwareProduct.UpdateSupplyChainInsight(
            parsed.RemediationPath,
            parsed.Confidence,
            parsed.SourceFormat,
            parsed.PrimaryComponentName,
            parsed.PrimaryComponentVersion,
            parsed.FixedVersion,
            parsed.AffectedVulnerabilityCount,
            parsed.Summary,
            DateTimeOffset.UtcNow
        );

        await dbContext.SaveChangesAsync(ct);

        return parsed;
    }

    public async Task<SupplyChainImportResult> ImportAsyncForNormalizedSoftware(
        Guid softwareProductId,
        string documentJson,
        CancellationToken ct
    )
    {
        var software = await dbContext
            .SoftwareProducts
            .FirstOrDefaultAsync(item => item.Id == softwareProductId, ct);
        if (software is null)
        {
            throw new InvalidOperationException("Software product was not found.");
        }

        var parsed = Parse(documentJson);
        software.UpdateSupplyChainInsight(
            parsed.RemediationPath,
            parsed.Confidence,
            parsed.SourceFormat,
            parsed.PrimaryComponentName,
            parsed.PrimaryComponentVersion,
            parsed.FixedVersion,
            parsed.AffectedVulnerabilityCount,
            parsed.Summary,
            DateTimeOffset.UtcNow
        );

        await dbContext.SaveChangesAsync(ct);

        return parsed;
    }

    internal static SupplyChainImportResult Parse(string documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            throw new ArgumentException("Document JSON is required.", nameof(documentJson));
        }

        using var document = JsonDocument.Parse(documentJson);
        var root = document.RootElement;

        var bomFormat = root.GetPropertyOrDefault("bomFormat");
        if (!string.Equals(bomFormat, "CycloneDX", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only CycloneDX JSON documents are supported.");
        }

        var rootComponent = ReadRootComponent(root);
        var components = ReadComponents(root, rootComponent);
        var statements = ReadStatements(root, components, rootComponent);

        if (statements.Count == 0)
        {
            throw new InvalidOperationException(
                "The CycloneDX document did not contain any vulnerability analysis statements."
            );
        }

        var affectedStatements = statements
            .Where(item =>
                item.State is SupplyChainAnalysisState.Affected
                    or SupplyChainAnalysisState.Fixed
                    or SupplyChainAnalysisState.UnderInvestigation
            )
            .ToList();

        if (affectedStatements.Count == 0)
        {
            throw new InvalidOperationException(
                "The CycloneDX document did not mark any vulnerability as affected, fixed, or under investigation."
            );
        }

        var dependencyStatement = affectedStatements.FirstOrDefault(item => !item.IsPrimaryComponent);
        var primaryStatement = affectedStatements.FirstOrDefault(item => item.IsPrimaryComponent);
        var representative = dependencyStatement ?? primaryStatement ?? affectedStatements[0];
        var fixedVersion = affectedStatements
            .Select(item => item.FixedVersion)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

        var remediationPath = ResolveRemediationPath(affectedStatements, fixedVersion);
        var confidence = ResolveConfidence(affectedStatements, representative);
        var sourceFormat = root.TryGetProperty("specVersion", out var specVersionElement)
            ? $"CycloneDX {specVersionElement.GetString() ?? string.Empty}".Trim()
            : "CycloneDX";
        var affectedVulnerabilityCount = affectedStatements
            .Select(item => item.VulnerabilityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new SupplyChainImportResult(
            remediationPath,
            confidence,
            sourceFormat,
            representative.ComponentName,
            representative.ComponentVersion,
            fixedVersion,
            affectedVulnerabilityCount,
            BuildSummary(remediationPath, representative, fixedVersion, affectedVulnerabilityCount)
        );
    }

    private static SupplyChainRemediationPath ResolveRemediationPath(
        IReadOnlyList<SupplyChainStatement> affectedStatements,
        string? fixedVersion
    )
    {
        if (affectedStatements.Any(item => item.State == SupplyChainAnalysisState.UnderInvestigation))
        {
            return SupplyChainRemediationPath.UnderInvestigation;
        }

        if (affectedStatements.Any(item => !item.IsPrimaryComponent))
        {
            return string.IsNullOrWhiteSpace(fixedVersion)
                ? SupplyChainRemediationPath.VendorUpdateRequired
                : SupplyChainRemediationPath.ProductUpgrade;
        }

        return SupplyChainRemediationPath.ProductUpgrade;
    }

    private static SupplyChainInsightConfidence ResolveConfidence(
        IReadOnlyList<SupplyChainStatement> affectedStatements,
        SupplyChainStatement representative
    )
    {
        if (
            affectedStatements.Any(item =>
                item.State is SupplyChainAnalysisState.Affected or SupplyChainAnalysisState.Fixed
            ) && !string.IsNullOrWhiteSpace(representative.ComponentName)
        )
        {
            return SupplyChainInsightConfidence.Confirmed;
        }

        return SupplyChainInsightConfidence.Likely;
    }

    private static string BuildSummary(
        SupplyChainRemediationPath remediationPath,
        SupplyChainStatement representative,
        string? fixedVersion,
        int affectedVulnerabilityCount
    )
    {
        var componentLabel = string.IsNullOrWhiteSpace(representative.ComponentVersion)
            ? representative.ComponentName
            : $"{representative.ComponentName} {representative.ComponentVersion}";

        return remediationPath switch
        {
            SupplyChainRemediationPath.VendorUpdateRequired =>
                $"{affectedVulnerabilityCount} vulnerabilit{(affectedVulnerabilityCount == 1 ? "y is" : "ies are")} tied to bundled component {componentLabel}. No fixed product release was declared, so the software maintainer likely needs to ship an updated version before users can fully remediate this.",
            SupplyChainRemediationPath.ProductUpgrade =>
                string.IsNullOrWhiteSpace(fixedVersion)
                    ? $"{affectedVulnerabilityCount} vulnerabilit{(affectedVulnerabilityCount == 1 ? "y is" : "ies are")} affecting {componentLabel}. The product should be remediated by upgrading to a fixed software release."
                    : $"{affectedVulnerabilityCount} vulnerabilit{(affectedVulnerabilityCount == 1 ? "y is" : "ies are")} affecting {componentLabel}. The evidence points to upgrading the product to a release that includes fix version {fixedVersion}.",
            _ =>
                $"{affectedVulnerabilityCount} vulnerabilit{(affectedVulnerabilityCount == 1 ? "y is" : "ies are")} still under investigation for {componentLabel}. More vendor guidance is needed before PatchHound can classify the remediation path confidently.",
        };
    }

    private static SupplyChainComponent ReadRootComponent(JsonElement root)
    {
        if (
            root.TryGetProperty("metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty("component", out var component)
            && component.ValueKind == JsonValueKind.Object
        )
        {
            return ReadComponent(component, isPrimaryComponent: true);
        }

        return new SupplyChainComponent("root", "product", null, true);
    }

    private static Dictionary<string, SupplyChainComponent> ReadComponents(
        JsonElement root,
        SupplyChainComponent rootComponent
    )
    {
        var components = new Dictionary<string, SupplyChainComponent>(StringComparer.OrdinalIgnoreCase)
        {
            [rootComponent.Reference] = rootComponent,
        };

        if (
            !root.TryGetProperty("components", out var componentsElement)
            || componentsElement.ValueKind != JsonValueKind.Array
        )
        {
            return components;
        }

        foreach (var componentElement in componentsElement.EnumerateArray())
        {
            if (componentElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var component = ReadComponent(componentElement, isPrimaryComponent: false);
            components[component.Reference] = component;
        }

        return components;
    }

    private static List<SupplyChainStatement> ReadStatements(
        JsonElement root,
        IReadOnlyDictionary<string, SupplyChainComponent> components,
        SupplyChainComponent rootComponent
    )
    {
        var results = new List<SupplyChainStatement>();
        if (
            !root.TryGetProperty("vulnerabilities", out var vulnerabilitiesElement)
            || vulnerabilitiesElement.ValueKind != JsonValueKind.Array
        )
        {
            return results;
        }

        foreach (var vulnerabilityElement in vulnerabilitiesElement.EnumerateArray())
        {
            if (vulnerabilityElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var vulnerabilityId = vulnerabilityElement.GetPropertyOrDefault("id") ?? "unknown";
            var recommendation = vulnerabilityElement.GetPropertyOrDefault("recommendation");
            var analysis = vulnerabilityElement.TryGetProperty("analysis", out var analysisElement)
                ? analysisElement
                : default;
            var state = ParseState(analysis.GetPropertyOrDefault("state"));
            var detail = analysis.GetPropertyOrDefault("detail");
            var fixedVersion = ExtractFixedVersion(recommendation) ?? ExtractFixedVersion(detail);

            if (
                !vulnerabilityElement.TryGetProperty("affects", out var affectsElement)
                || affectsElement.ValueKind != JsonValueKind.Array
            )
            {
                results.Add(
                    new SupplyChainStatement(
                        vulnerabilityId,
                        rootComponent.Name,
                        rootComponent.Version,
                        state,
                        recommendation,
                        fixedVersion,
                        IsPrimaryComponent: true
                    )
                );
                continue;
            }

            foreach (var affectElement in affectsElement.EnumerateArray())
            {
                var reference =
                    affectElement.GetPropertyOrDefault("ref")
                    ?? affectElement.GetNestedPropertyOrDefault("target", "ref")
                    ?? rootComponent.Reference;
                var component = components.GetValueOrDefault(reference)
                    ?? new SupplyChainComponent(reference, reference, null, false);

                results.Add(
                    new SupplyChainStatement(
                        vulnerabilityId,
                        component.Name,
                        component.Version,
                        state,
                        recommendation,
                        fixedVersion,
                        component.IsPrimaryComponent
                    )
                );
            }
        }

        return results;
    }

    private static SupplyChainComponent ReadComponent(
        JsonElement component,
        bool isPrimaryComponent
    )
    {
        var reference =
            component.GetPropertyOrDefault("bom-ref")
            ?? component.GetPropertyOrDefault("purl")
            ?? component.GetPropertyOrDefault("name")
            ?? Guid.NewGuid().ToString("N");

        return new SupplyChainComponent(
            reference,
            component.GetPropertyOrDefault("name") ?? reference,
            component.GetPropertyOrDefault("version"),
            isPrimaryComponent
        );
    }

    private static SupplyChainAnalysisState ParseState(string? state) =>
        state?.Trim().ToLowerInvariant() switch
        {
            "affected" => SupplyChainAnalysisState.Affected,
            "fixed" => SupplyChainAnalysisState.Fixed,
            "under_investigation" => SupplyChainAnalysisState.UnderInvestigation,
            "under-investigation" => SupplyChainAnalysisState.UnderInvestigation,
            _ => SupplyChainAnalysisState.Unknown,
        };

    private static string? ExtractFixedVersion(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = VersionRegex.Match(input);
        return match.Success ? match.Groups["version"].Value : null;
    }

    public sealed record SupplyChainImportResult(
        SupplyChainRemediationPath RemediationPath,
        SupplyChainInsightConfidence Confidence,
        string SourceFormat,
        string PrimaryComponentName,
        string? PrimaryComponentVersion,
        string? FixedVersion,
        int AffectedVulnerabilityCount,
        string Summary
    );

    private sealed record SupplyChainComponent(
        string Reference,
        string Name,
        string? Version,
        bool IsPrimaryComponent
    );

    private sealed record SupplyChainStatement(
        string VulnerabilityId,
        string ComponentName,
        string? ComponentVersion,
        SupplyChainAnalysisState State,
        string? Recommendation,
        string? FixedVersion,
        bool IsPrimaryComponent
    );

    private enum SupplyChainAnalysisState
    {
        Unknown,
        Affected,
        Fixed,
        UnderInvestigation,
    }
}

internal static class JsonElementSupplyChainExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    public static string? GetNestedPropertyOrDefault(
        this JsonElement element,
        string objectProperty,
        string propertyName
    )
    {
        if (
            element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(objectProperty, out var nested)
            || nested.ValueKind != JsonValueKind.Object
        )
        {
            return null;
        }

        return nested.GetPropertyOrDefault(propertyName);
    }
}
