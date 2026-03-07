namespace PatchHound.Api.Models;

public record PaginationQuery(int Page = 1, int PageSize = 25)
{
    private const int MaxPageSize = 100;
    public int BoundedPageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (Math.Max(Page, 1) - 1) * BoundedPageSize;
}
