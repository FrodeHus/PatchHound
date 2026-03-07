namespace PatchHound.Api.Models;

public record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount);
