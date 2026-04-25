using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Infrastructure.Credentials;

public class StoredCredentialResolver(
    PatchHoundDbContext dbContext,
    ISecretStore secretStore
)
{
    public async Task<EntraStoredCredential?> ResolveEntraClientSecretAsync(
        Guid credentialId,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var credential = await dbContext.StoredCredentials.AsNoTracking()
            .Include(item => item.TenantScopes)
            .FirstOrDefaultAsync(
                item =>
                    item.Id == credentialId
                    && item.Type == StoredCredentialTypes.EntraClientSecret
                    && (item.IsGlobal || item.TenantScopes.Any(scope => scope.TenantId == tenantId)),
                ct
            );

        if (credential is null)
            return null;

        var clientSecret = await secretStore.GetSecretAsync(
            credential.SecretRef,
            StoredCredentialSecretKeys.ClientSecret,
            ct
        ) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(credential.CredentialTenantId)
            || string.IsNullOrWhiteSpace(credential.ClientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        return new EntraStoredCredential(
            credential.CredentialTenantId,
            credential.ClientId,
            clientSecret
        );
    }

    public async Task<EntraStoredCredential?> ResolveGlobalEntraClientSecretAsync(
        Guid credentialId,
        CancellationToken ct
    )
    {
        var credential = await dbContext.StoredCredentials.AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.Id == credentialId
                    && item.Type == StoredCredentialTypes.EntraClientSecret
                    && item.IsGlobal,
                ct
            );

        if (credential is null)
            return null;

        var clientSecret = await secretStore.GetSecretAsync(
            credential.SecretRef,
            StoredCredentialSecretKeys.ClientSecret,
            ct
        ) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(credential.CredentialTenantId)
            || string.IsNullOrWhiteSpace(credential.ClientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        return new EntraStoredCredential(
            credential.CredentialTenantId,
            credential.ClientId,
            clientSecret
        );
    }

    public async Task<string?> ResolveGlobalApiKeyAsync(Guid credentialId, CancellationToken ct)
    {
        var credential = await dbContext.StoredCredentials.AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.Id == credentialId
                    && item.Type == StoredCredentialTypes.ApiKey
                    && item.IsGlobal,
                ct
            );

        if (credential is null)
            return null;

        var apiKey = await secretStore.GetSecretAsync(
            credential.SecretRef,
            StoredCredentialSecretKeys.ApiKey,
            ct
        ) ?? string.Empty;

        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }
}

public sealed record EntraStoredCredential(
    string TenantId,
    string ClientId,
    string ClientSecret
);

public static class StoredCredentialSecretKeys
{
    public const string ApiKey = "apiKey";
    public const string ClientSecret = "clientSecret";
}
