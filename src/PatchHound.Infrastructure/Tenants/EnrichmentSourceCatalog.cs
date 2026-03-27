using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Tenants;

public static class EnrichmentSourceCatalog
{
    public const string NvdSourceKey = "nvd";
    public const string DefenderSourceKey = TenantSourceCatalog.DefenderSourceKey;
    public const string EndOfLifeSourceKey = "endoflife";
    public const string DefaultNvdApiBaseUrl = "https://services.nvd.nist.gov";
    public const string DefaultEndOfLifeApiBaseUrl = "https://endoflife.date";
    public const int DefaultDefenderRefreshTtlHours = 24;

    public static IReadOnlyList<EnrichmentSourceConfiguration> CreateDefaults()
    {
        return [CreateDefaultDefender(), CreateDefaultNvd(), CreateDefaultEndOfLife()];
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

    public static bool HasConfiguredCredentials(EnrichmentSourceConfiguration source)
    {
        if (
            string.Equals(source.SourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.SourceKey, EndOfLifeSourceKey, StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(source.SecretRef);
    }

    public static bool RequiresCredentials(string sourceKey)
    {
        return !string.Equals(sourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceKey, EndOfLifeSourceKey, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSecretKeyName(string sourceKey)
    {
        return string.Equals(sourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            ? "apiKey"
            : "secret";
    }
}
