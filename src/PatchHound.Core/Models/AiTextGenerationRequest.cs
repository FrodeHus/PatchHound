namespace PatchHound.Core.Models;

public record AiTextGenerationRequest(
    string SystemPrompt,
    string UserPrompt
);
