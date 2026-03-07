using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Tenants;

public static class EnrichmentSourceCatalog
{
    public const string NvdSourceKey = "nvd";
    public const string DefaultNvdApiBaseUrl = "https://services.nvd.nist.gov";

    public static IReadOnlyList<EnrichmentSourceConfiguration> CreateDefaults()
    {
        return [CreateDefaultNvd()];
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

    public static bool HasConfiguredCredentials(EnrichmentSourceConfiguration source)
    {
        return !string.IsNullOrWhiteSpace(source.SecretRef);
    }

    public static string GetSecretKeyName(string sourceKey)
    {
        return string.Equals(sourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            ? "apiKey"
            : "secret";
    }
}
