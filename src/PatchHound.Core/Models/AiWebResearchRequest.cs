namespace PatchHound.Core.Models;

public record AiWebResearchRequest(
    string Query,
    IReadOnlyList<string> AllowedDomains,
    int MaxSources,
    bool IncludeCitations
);
