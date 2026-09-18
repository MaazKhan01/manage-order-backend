namespace DmOrder.Application.Common.Models;

/// <summary>
/// Pagination input, clamped on construction. Clamping rather than rejecting keeps list endpoints
/// forgiving for clients while making it impossible to ask the database for an unbounded result set.
/// </summary>
public readonly record struct PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public PageRequest(int? page, int? pageSize)
    {
        Page = page is null or < 1 ? 1 : page.Value;
        PageSize = pageSize switch
        {
            null or < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value,
        };
    }

    public int Page { get; }

    public int PageSize { get; }
}
