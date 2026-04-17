using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface ICredentialBearingAssetSource
{
    string SourceKey { get; }
    string SourceName { get; }
    Task<IngestionCredentialAssetSnapshot> FetchCredentialAssetsAsync(
        Guid tenantId,
        CancellationToken ct);
}
