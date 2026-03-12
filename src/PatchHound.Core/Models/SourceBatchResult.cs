namespace PatchHound.Core.Models;

public sealed record SourceBatchResult<TItem>(
    IReadOnlyList<TItem> Items,
    string? NextCursorJson,
    bool IsComplete
);
