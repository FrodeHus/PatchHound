using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ConnectionProfileSecretWriter
{
    private readonly ISecretStore _secretStore;

    public ConnectionProfileSecretWriter(ISecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    public static string BuildPath(Guid tenantId, Guid profileId) =>
        $"tenants/{tenantId}/auth-scan-connections/{profileId}";

    public async Task<string> WritePasswordAsync(Guid tenantId, Guid profileId, string password, CancellationToken ct)
    {
        var path = BuildPath(tenantId, profileId);
        await _secretStore.PutSecretAsync(path, new Dictionary<string, string> { ["password"] = password }, ct);
        return path;
    }

    public async Task<string> WritePrivateKeyAsync(
        Guid tenantId, Guid profileId, string privateKey, string? passphrase, CancellationToken ct)
    {
        var path = BuildPath(tenantId, profileId);
        var dict = new Dictionary<string, string> { ["privateKey"] = privateKey };
        if (!string.IsNullOrEmpty(passphrase)) dict["passphrase"] = passphrase;
        await _secretStore.PutSecretAsync(path, dict, ct);
        return path;
    }

    public async Task DeleteAsync(Guid tenantId, Guid profileId, CancellationToken ct)
    {
        await _secretStore.DeleteSecretPathAsync(BuildPath(tenantId, profileId), ct);
    }
}
