using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IAssetInventoryBatchSource
{
    Task<SourceBatchResult<IngestionAssetInventorySnapshot>> FetchAssetBatchAsync(
        Guid tenantId,
        string? cursorJson,
        int batchSize,
        CancellationToken ct
    );
}
