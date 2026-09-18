using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Common.Models;

/// <summary>Standard envelope for every list endpoint. Clients can rely on this shape everywhere.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;
}

public static class PagedResultExtensions
{
    /// <summary>
    /// Runs a count and a single page query against the database. The caller is responsible for
    /// having already applied store scoping and ordering — an unordered page is a non-deterministic page.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page.Page, page.PageSize, totalCount);
    }
}
