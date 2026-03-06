namespace Vigil.Api.Models;

public record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount);
