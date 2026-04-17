using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Tenants;

public static class TenantSourceCatalog
{
    public const string DefenderSourceKey = "microsoft-defender";
    public const string DefaultDefenderSchedule = "0 */6 * * *";
    public const string DefaultDefenderApiBaseUrl = "https://api.securitycenter.microsoft.com";
    public const string DefaultDefenderTokenScope =
        "https://api.securitycenter.microsoft.com/.default";

    public const string EntraApplicationsSourceKey = "entra-applications";
    public const string DefaultEntraApplicationsSchedule = "0 */6 * * *";
    public const string DefaultEntraApplicationsApiBaseUrl = "https://graph.microsoft.com";
    public const string DefaultEntraApplicationsTokenScope =
        "https://graph.microsoft.com/.default";

    public static IReadOnlyList<TenantSourceConfiguration> CreateDefaults(Guid tenantId)
    {
        return [CreateDefaultDefender(tenantId), CreateDefaultEntraApplications(tenantId)];
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

    public static TenantSourceConfiguration CreateDefaultEntraApplications(Guid tenantId)
    {
        return TenantSourceConfiguration.Create(
            tenantId,
            EntraApplicationsSourceKey,
            "Microsoft Entra Applications",
            false,
            DefaultEntraApplicationsSchedule,
            apiBaseUrl: DefaultEntraApplicationsApiBaseUrl,
            tokenScope: DefaultEntraApplicationsTokenScope
        );
    }

    public static bool HasConfiguredCredentials(TenantSourceConfiguration source)
    {
        if (!string.IsNullOrWhiteSpace(source.LinkedSourceKey))
            return true;

        return !string.IsNullOrWhiteSpace(source.CredentialTenantId)
            && !string.IsNullOrWhiteSpace(source.ClientId)
            && !string.IsNullOrWhiteSpace(source.SecretRef);
    }

    public static bool SupportsScheduling(TenantSourceConfiguration source)
    {
        return source.SourceKey is DefenderSourceKey or EntraApplicationsSourceKey;
    }

    public static bool SupportsManualSync(TenantSourceConfiguration source)
    {
        return SupportsScheduling(source);
    }

    public static string GetSecretKeyName(string sourceKey)
    {
        return "clientSecret";
    }
}
