using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IAssetInventorySource
{
    Task<IReadOnlyList<IngestionAsset>> FetchAssetsAsync(Guid tenantId, CancellationToken ct);
}
