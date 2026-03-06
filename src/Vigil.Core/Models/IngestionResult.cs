using Vigil.Core.Enums;

namespace Vigil.Core.Models;

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

public record IngestionAffectedAsset(string ExternalAssetId, string AssetName, AssetType AssetType);
