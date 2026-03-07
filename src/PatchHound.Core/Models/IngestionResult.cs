using PatchHound.Core.Enums;

namespace PatchHound.Core.Models;

public record IngestionResult(
    string ExternalId,
    string Title,
    string Description,
    Severity VendorSeverity,
    decimal? CvssScore,
    string? CvssVector,
    DateTimeOffset? PublishedDate,
    IReadOnlyList<IngestionAffectedAsset> AffectedAssets
);

public record IngestionAffectedAsset(
    string ExternalAssetId,
    string AssetName,
    AssetType AssetType
);

public record IngestionAsset(
    string ExternalId,
    string Name,
    AssetType AssetType,
    string? Description = null,
    string Metadata = "{}"
);
