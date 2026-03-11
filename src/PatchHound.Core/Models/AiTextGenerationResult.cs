namespace PatchHound.Core.Models;

public record AiTextGenerationResult(
    Guid TenantAiProfileId,
    string Content,
    string ProviderType,
    string ProfileName,
    string Model,
    DateTimeOffset GeneratedAt
);
