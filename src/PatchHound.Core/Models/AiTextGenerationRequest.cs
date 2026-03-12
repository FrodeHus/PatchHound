namespace PatchHound.Core.Models;

public record AiTextGenerationRequest(
    string SystemPrompt,
    string UserPrompt,
    string? ExternalContext = null,
    bool UseProviderNativeWebResearch = false,
    IReadOnlyList<string>? AllowedDomains = null,
    int? MaxResearchSources = null,
    bool IncludeCitations = true
);
