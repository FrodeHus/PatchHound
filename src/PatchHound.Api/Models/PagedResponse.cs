namespace PatchHound.Api.Models;

public record PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }

    public PagedResponse(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = Math.Max(page, 1);
        PageSize = Math.Max(pageSize, 1);
        TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)PageSize);
    }
}
