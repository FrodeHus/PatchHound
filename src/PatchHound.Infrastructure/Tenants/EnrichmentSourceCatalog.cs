using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Tenants;

public static class EnrichmentSourceCatalog
{
    public const string NvdSourceKey = "nvd";
    public const string DefenderSourceKey = TenantSourceCatalog.DefenderSourceKey;
    public const string EndOfLifeSourceKey = "endoflife";
    public const string SupplyChainSourceKey = "supply-chain";
    public const string DefaultNvdApiBaseUrl = "https://services.nvd.nist.gov/rest/json/cves/2.0";
    public const string DefaultEndOfLifeApiBaseUrl = "https://endoflife.date";
    public const int DefaultDefenderRefreshTtlHours = 24;
    public const int DefaultSupplyChainRefreshTtlHours = 24;

    public static IReadOnlyList<EnrichmentSourceConfiguration> CreateDefaults()
    {
        return [CreateDefaultDefender(), CreateDefaultNvd(), CreateDefaultEndOfLife(), CreateDefaultSupplyChain()];
    }

    public static EnrichmentSourceConfiguration CreateDefaultDefender()
    {
        return EnrichmentSourceConfiguration.Create(
            DefenderSourceKey,
            "Microsoft Defender",
            false,
            apiBaseUrl: TenantSourceCatalog.DefaultDefenderApiBaseUrl,
            refreshTtlHours: DefaultDefenderRefreshTtlHours
        );
    }

    public static EnrichmentSourceConfiguration CreateDefaultNvd()
    {
        return EnrichmentSourceConfiguration.Create(
            NvdSourceKey,
            "NVD API",
            false,
            apiBaseUrl: DefaultNvdApiBaseUrl
        );
    }

    public static EnrichmentSourceConfiguration CreateDefaultEndOfLife()
    {
        return EnrichmentSourceConfiguration.Create(
            EndOfLifeSourceKey,
            "Software End of Life",
            false,
            apiBaseUrl: DefaultEndOfLifeApiBaseUrl
        );
    }

    public static EnrichmentSourceConfiguration CreateDefaultSupplyChain()
    {
        return EnrichmentSourceConfiguration.Create(
            SupplyChainSourceKey,
            "Supply Chain Evidence",
            false,
            apiBaseUrl: string.Empty,
            refreshTtlHours: DefaultSupplyChainRefreshTtlHours
        );
    }

    public static bool HasConfiguredCredentials(EnrichmentSourceConfiguration source)
    {
        if (
            string.Equals(source.SourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.SourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.SourceKey, EndOfLifeSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.SourceKey, SupplyChainSourceKey, StringComparison.OrdinalIgnoreCase)
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
            && !string.Equals(sourceKey, SupplyChainSourceKey, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSecretKeyName(string sourceKey)
    {
        return string.Equals(sourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            ? "apiKey"
            : "secret";
    }
}
