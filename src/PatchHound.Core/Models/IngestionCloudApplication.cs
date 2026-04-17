namespace PatchHound.Core.Models;

public record IngestionCloudApplication(
    string ExternalId,
    string Name,
    string? Description,
    string? AppId,
    bool IsFallbackPublicClient,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<IngestionCloudApplicationCredential> Credentials
);

public record IngestionCloudApplicationCredential(
    string ExternalId,
    string Type,
    string? DisplayName,
    DateTimeOffset ExpiresAt
);

public record IngestionCloudApplicationSnapshot(
    IReadOnlyList<IngestionCloudApplication> Applications
);
