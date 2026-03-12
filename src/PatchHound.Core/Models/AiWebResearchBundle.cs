namespace PatchHound.Core.Models;

public record AiWebResearchBundle(
    string Context,
    IReadOnlyList<AiWebResearchSource> Sources
);

public record AiWebResearchSource(
    string Title,
    string Url,
    string? Snippet
);
