namespace PatchHound.Infrastructure.Options;

public class AiResearchOptions
{
    public const string SectionName = "AiResearch";

    public string JinaSearchProvider { get; set; } = "Google";
}
