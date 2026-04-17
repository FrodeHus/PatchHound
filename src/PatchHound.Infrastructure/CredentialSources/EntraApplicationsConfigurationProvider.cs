using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.CredentialSources;

public class EntraApplicationsConfigurationProvider(
    PatchHoundDbContext dbContext,
    ISecretStore secretStore
)
{
    public virtual async Task<EntraClientConfiguration?> GetConfigurationAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        var source = await dbContext.TenantSourceConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId
                    && s.SourceKey == TenantSourceCatalog.EntraApplicationsSourceKey,
                ct
            );

        if (source is null || !source.Enabled)
            return null;

        // When linked to another source, resolve credentials from it
        if (!string.IsNullOrWhiteSpace(source.LinkedSourceKey))
            return await ResolveFromLinkedSourceAsync(tenantId, source.LinkedSourceKey, source.ApiBaseUrl, source.TokenScope, ct);

        if (!TenantSourceCatalog.HasConfiguredCredentials(source))
        {
            var hasAnyCredentialInput =
                !string.IsNullOrWhiteSpace(source.CredentialTenantId)
                || !string.IsNullOrWhiteSpace(source.ClientId)
                || !string.IsNullOrWhiteSpace(source.SecretRef);

            if (hasAnyCredentialInput)
                throw new IngestionTerminalException(
                    "Entra Applications source is enabled but credentials are incomplete."
                );

            return null;
        }

        var clientSecret = string.Empty;
        if (!string.IsNullOrWhiteSpace(source.SecretRef))
            clientSecret = await secretStore.GetSecretAsync(source.SecretRef, "clientSecret", ct) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(source.CredentialTenantId)
            || string.IsNullOrWhiteSpace(source.ClientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new IngestionTerminalException(
                "Entra Applications source credentials could not be resolved."
            );
        }

        return new EntraClientConfiguration(
            source.CredentialTenantId,
            source.ClientId,
            clientSecret,
            source.ApiBaseUrl,
            source.TokenScope
        );
    }

    private async Task<EntraClientConfiguration?> ResolveFromLinkedSourceAsync(
        Guid tenantId,
        string linkedSourceKey,
        string apiBaseUrl,
        string tokenScope,
        CancellationToken ct
    )
    {
        var linked = await dbContext.TenantSourceConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SourceKey == linkedSourceKey, ct);

        if (linked is null || !TenantSourceCatalog.HasConfiguredCredentials(linked))
            throw new IngestionTerminalException(
                $"Entra Applications source is linked to '{linkedSourceKey}' but that source has no configured credentials."
            );

        var clientSecret = string.Empty;
        if (!string.IsNullOrWhiteSpace(linked.SecretRef))
            clientSecret = await secretStore.GetSecretAsync(linked.SecretRef, "clientSecret", ct) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(linked.CredentialTenantId)
            || string.IsNullOrWhiteSpace(linked.ClientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new IngestionTerminalException(
                $"Entra Applications source could not resolve credentials from linked source '{linkedSourceKey}'."
            );
        }

        return new EntraClientConfiguration(
            linked.CredentialTenantId,
            linked.ClientId,
            clientSecret,
            string.IsNullOrWhiteSpace(apiBaseUrl) ? TenantSourceCatalog.DefaultEntraApplicationsApiBaseUrl : apiBaseUrl,
            string.IsNullOrWhiteSpace(tokenScope) ? TenantSourceCatalog.DefaultEntraApplicationsTokenScope : tokenScope
        );
    }
}
