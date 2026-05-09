namespace HostPanel.Core.DTOs;

public record PaginatedRequest(int Page = 1, int PageSize = 25, string? SortBy = null, bool SortDesc = false);

public record PaginatedResponse<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
)
{
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}

public static class QueryableExtensions
{
    public static async Task<PaginatedResponse<T>> ToPaginatedAsync<T>(
        this IQueryable<T> query,
        int page, int pageSize,
        CancellationToken ct = default) where T : class
    {
        var totalCount = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(query, ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var items = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            query.Skip((page - 1) * pageSize).Take(pageSize), ct);

        return new PaginatedResponse<T>(items, totalCount, page, pageSize, totalPages);
    }
}
