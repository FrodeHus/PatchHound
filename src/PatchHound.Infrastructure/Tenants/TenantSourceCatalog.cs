using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Tenants;

public static class TenantSourceCatalog
{
    public const string DefenderSourceKey = "microsoft-defender";
    public const string NvdSourceKey = "nvd";
    public const string DefaultDefenderSchedule = "0 */6 * * *";
    public const string DefaultDefenderApiBaseUrl = "https://api.securitycenter.microsoft.com";
    public const string DefaultDefenderTokenScope = "https://api.securitycenter.microsoft.com/.default";
    public const string DefaultNvdApiBaseUrl = "https://services.nvd.nist.gov";

    public static IReadOnlyList<TenantSourceConfiguration> CreateDefaults(Guid tenantId)
    {
        return
        [
            CreateDefaultDefender(tenantId),
            CreateDefaultNvd(tenantId),
        ];
    }

    public static TenantSourceConfiguration CreateDefaultDefender(Guid tenantId)
    {
        return TenantSourceConfiguration.Create(
            tenantId,
            DefenderSourceKey,
            "Microsoft Defender",
            false,
            DefaultDefenderSchedule,
            apiBaseUrl: DefaultDefenderApiBaseUrl,
            tokenScope: DefaultDefenderTokenScope
        );
    }

    public static TenantSourceConfiguration CreateDefaultNvd(Guid tenantId)
    {
        return TenantSourceConfiguration.Create(
            tenantId,
            NvdSourceKey,
            "NVD API",
            false,
            string.Empty,
            apiBaseUrl: DefaultNvdApiBaseUrl
        );
    }

    public static bool HasConfiguredCredentials(TenantSourceConfiguration source)
    {
        return string.Equals(source.SourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(source.SecretRef)
            : !string.IsNullOrWhiteSpace(source.CredentialTenantId)
                && !string.IsNullOrWhiteSpace(source.ClientId)
                && !string.IsNullOrWhiteSpace(source.SecretRef);
    }

    public static bool SupportsScheduling(TenantSourceConfiguration source)
    {
        return string.Equals(source.SourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SupportsManualSync(TenantSourceConfiguration source)
    {
        return SupportsScheduling(source);
    }

    public static string GetSecretKeyName(string sourceKey)
    {
        return string.Equals(sourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
            ? "apiKey"
            : "clientSecret";
    }
}
