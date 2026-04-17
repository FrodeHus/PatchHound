namespace PatchHound.Core.Models;

public record IngestionCredentialAsset(
    string ExternalId,
    string Name,
    string AssetType,
    string? Description,
    IReadOnlyList<IngestionCredential> Credentials,
    string Metadata = "{}"
);

public record IngestionCredential(
    string ExternalId,
    string Type,
    string? DisplayName,
    DateTimeOffset ExpiresAt
);

public record IngestionCredentialAssetSnapshot(
    IReadOnlyList<IngestionCredentialAsset> Assets
);
