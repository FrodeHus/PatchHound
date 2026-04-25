using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.CredentialSources;

public class EntraApplicationsConfigurationProvider(
    PatchHoundDbContext dbContext,
    StoredCredentialResolver credentialResolver
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

        if (!source.StoredCredentialId.HasValue)
        {
            return null;
        }

        var credential = await credentialResolver.ResolveEntraClientSecretAsync(
            source.StoredCredentialId.Value,
            tenantId,
            ct
        );
        if (credential is null)
        {
            throw new IngestionTerminalException(
                "Entra Applications source credential could not be resolved or is not available to this tenant."
            );
        }

        return new EntraClientConfiguration(
            credential.TenantId,
            credential.ClientId,
            credential.ClientSecret,
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

        if (linked is null || !linked.StoredCredentialId.HasValue)
            throw new IngestionTerminalException(
                $"Entra Applications source is linked to '{linkedSourceKey}' but that source has no configured credentials."
            );

        var credential = await credentialResolver.ResolveEntraClientSecretAsync(
            linked.StoredCredentialId.Value,
            tenantId,
            ct
        );
        if (credential is null)
        {
            throw new IngestionTerminalException(
                $"Entra Applications source could not resolve credentials from linked source '{linkedSourceKey}'."
            );
        }

        return new EntraClientConfiguration(
            credential.TenantId,
            credential.ClientId,
            credential.ClientSecret,
            string.IsNullOrWhiteSpace(apiBaseUrl) ? TenantSourceCatalog.DefaultEntraApplicationsApiBaseUrl : apiBaseUrl,
            string.IsNullOrWhiteSpace(tokenScope) ? TenantSourceCatalog.DefaultEntraApplicationsTokenScope : tokenScope
        );
    }
}
