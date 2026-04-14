namespace PatchHound.Core.Models;

public record SoftwareDescriptionGenerationResult(
    Guid SoftwareProductId,
    string Description,
    string ProviderType,
    string ProfileName,
    string Model,
    DateTimeOffset GeneratedAt
);
