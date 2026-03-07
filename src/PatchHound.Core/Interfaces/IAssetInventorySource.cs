using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IAssetInventorySource
{
    Task<IngestionAssetInventorySnapshot> FetchAssetsAsync(Guid tenantId, CancellationToken ct);
}
