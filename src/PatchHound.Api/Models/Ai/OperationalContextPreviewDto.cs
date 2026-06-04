using PatchHound.Core.Models.OperationalContext;

namespace PatchHound.Api.Models.Ai;

public record OperationalContextPreviewDto(
    string ContextKind,
    Guid SubjectId,
    int TokenEstimate,
    bool Truncated,
    DateTimeOffset GeneratedAt,
    OperationalContextPack Context,
    IReadOnlyList<OperationalContextCitation> Citations);
