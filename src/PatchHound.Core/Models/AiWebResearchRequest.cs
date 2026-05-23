using PatchHound.Core.Enums;

namespace PatchHound.Core.Models;

public record AiWebResearchRequest(
    string Query,
    IReadOnlyList<string> AllowedDomains,
    int MaxSources,
    bool IncludeCitations,
    IReadOnlyList<Guid>? VulnerabilityIds = null,
    IReadOnlyList<AiResearchProviderKind>? Providers = null,
    string ResearchSourceKey = ""
);
