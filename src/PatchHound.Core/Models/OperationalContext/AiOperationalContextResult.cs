namespace PatchHound.Core.Models.OperationalContext;

public sealed class AiOperationalContextResult
{
    public required OperationalContextPack Pack { get; init; }
    public required string PackJson { get; init; }
    public IReadOnlyList<OperationalContextCitation> Citations { get; init; } = [];
    public int TokenEstimate { get; init; }
    public bool Truncated { get; init; }
}
