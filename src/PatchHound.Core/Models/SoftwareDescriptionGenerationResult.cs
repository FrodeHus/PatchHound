namespace PatchHound.Core.Models;

public record SoftwareDescriptionGenerationResult(
    Guid TenantSoftwareId,
    Guid NormalizedSoftwareId,
    string Description,
    string ProviderType,
    string ProfileName,
    string Model,
    DateTimeOffset GeneratedAt
);
