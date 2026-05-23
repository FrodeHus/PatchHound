using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Credentials;

namespace PatchHound.Infrastructure.Tenants;

public static class EnrichmentSourceCatalog
{
    public const string ScheduledTarget = "Scheduled";
    public const string AiResearchTarget = "AIResearch";
    public const string NvdSourceKey = "nvd";
    public const string DefenderSourceKey = TenantSourceCatalog.DefenderSourceKey;
    public const string EndOfLifeSourceKey = "endoflife";
    public const string SupplyChainSourceKey = "supply-chain";
    public const string JinaReaderSourceKey = "jina-reader";
    public const string ExternalWebSearchSourceKey = "external-web-search";
    public const string DefaultNvdApiBaseUrl = "https://services.nvd.nist.gov/rest/json/cves/2.0";
    public const string DefaultEndOfLifeApiBaseUrl = "https://endoflife.date";
    public const string DefaultJinaReaderApiBaseUrl = "https://r.jina.ai";
    public const int DefaultDefenderRefreshTtlHours = 24;
    public const int DefaultSupplyChainRefreshTtlHours = 24;

    public static IReadOnlyList<EnrichmentSourceConfiguration> CreateDefaults()
    {
        return [CreateDefaultDefender(), CreateDefaultNvd(), CreateDefaultEndOfLife(), CreateDefaultSupplyChain(), CreateDefaultJinaReader()];
    }

    public static EnrichmentSourceConfiguration CreateDefaultDefender()
    {
        return EnrichmentSourceConfiguration.Create(
            DefenderSourceKey,
            "Microsoft Defender",
            false,
            apiBaseUrl: TenantSourceCatalog.DefaultDefenderApiBaseUrl,
            refreshTtlHours: DefaultDefenderRefreshTtlHours,
            targets: ScheduledTarget
        );
    }

    public static EnrichmentSourceConfiguration CreateDefaultNvd()
    {
        return EnrichmentSourceConfiguration.Create(
            NvdSourceKey,
            "NVD API",
            false,
            apiBaseUrl: DefaultNvdApiBaseUrl,
            targets: ScheduledTarget
        );
    }

    public static EnrichmentSourceConfiguration CreateDefaultEndOfLife()
    {
        return EnrichmentSourceConfiguration.Create(
            EndOfLifeSourceKey,
            "Software End of Life",
            false,
            apiBaseUrl: DefaultEndOfLifeApiBaseUrl,
            targets: ScheduledTarget
        );
    }

    public static EnrichmentSourceConfiguration CreateDefaultSupplyChain()
    {
        return EnrichmentSourceConfiguration.Create(
            SupplyChainSourceKey,
            "Supply Chain Evidence",
            false,
            apiBaseUrl: string.Empty,
            refreshTtlHours: DefaultSupplyChainRefreshTtlHours,
            targets: ScheduledTarget
        );
    }

    public static EnrichmentSourceConfiguration CreateDefaultJinaReader()
    {
        return EnrichmentSourceConfiguration.Create(
            JinaReaderSourceKey,
            "Jina Reader",
            false,
            apiBaseUrl: DefaultJinaReaderApiBaseUrl,
            targets: AiResearchTarget,
            optionsJson: JinaReaderOptions.Default.ToJson()
        );
    }

    public static bool HasConfiguredCredentials(EnrichmentSourceConfiguration source)
    {
        if (
            string.Equals(source.SourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.SourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.SourceKey, EndOfLifeSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.SourceKey, SupplyChainSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.SourceKey, JinaReaderSourceKey, StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(source.SecretRef);
    }

    public static bool RequiresCredentials(string sourceKey)
    {
        return !string.Equals(sourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceKey, EndOfLifeSourceKey, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceKey, SupplyChainSourceKey, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceKey, JinaReaderSourceKey, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SupportsOptionalCredentials(string sourceKey)
    {
        return string.Equals(sourceKey, JinaReaderSourceKey, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSecretKeyName(string sourceKey)
    {
        return string.Equals(sourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceKey, JinaReaderSourceKey, StringComparison.OrdinalIgnoreCase)
            ? "apiKey"
            : "secret";
    }

    public static IReadOnlyList<string> GetAcceptedCredentialTypes(string sourceKey)
    {
        if (string.Equals(sourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase))
            return [StoredCredentialTypes.EntraClientSecret];

        if (string.Equals(sourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase))
            return [StoredCredentialTypes.ApiKey];

        if (string.Equals(sourceKey, JinaReaderSourceKey, StringComparison.OrdinalIgnoreCase))
            return [StoredCredentialTypes.ApiKey];

        return [];
    }

    public static bool HasTarget(EnrichmentSourceConfiguration source, string target)
    {
        return ParseTargets(source.Targets)
            .Contains(target, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ParseTargets(string? targets)
    {
        return (targets ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeTargets(IEnumerable<string>? targets)
    {
        var normalized = (targets ?? [])
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => target.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? ScheduledTarget : string.Join(",", normalized);
    }
}
