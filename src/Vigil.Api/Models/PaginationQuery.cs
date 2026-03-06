namespace Vigil.Api.Models;

public record PaginationQuery(int Page = 1, int PageSize = 25)
{
    public int Skip => (Page - 1) * PageSize;
}
