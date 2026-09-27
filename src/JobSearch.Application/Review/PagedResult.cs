namespace JobSearch.Application.Review;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
}
