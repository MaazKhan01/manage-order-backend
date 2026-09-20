using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Stores;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Admin;

/// <summary>
/// Filter for the admin's store list. <c>null</c> means "all", which is the default — an admin
/// opening the page wants the whole platform, not a slice of it.
/// </summary>
public enum AdminStoreFilter
{
    All = 0,
    Live = 1,
    Draft = 2,
    Suspended = 3,
}

public sealed record AdminStoreListQuery(
    int? Page,
    int? PageSize,
    string? Search,
    AdminStoreFilter? Filter,
    string? Sort);

public sealed class ListAdminStoresHandler(IAppDbContext db, IUserDirectory users)
{
    public async Task<PagedResult<AdminStoreListItemResponse>> HandleAsync(
        AdminStoreListQuery query,
        CancellationToken cancellationToken)
    {
        var stores = db.Stores.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);

            // The admin knows a store by its name or the link a seller sent them, not by id.
            stores = stores.Where(s =>
                EF.Functions.Like(s.Name.ToLower(), pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.Like(s.Slug, pattern, SearchPattern.EscapeCharacter));
        }

        stores = query.Filter switch
        {
            AdminStoreFilter.Live => stores.Where(s => s.IsPublished && s.IsActive),
            AdminStoreFilter.Draft => stores.Where(s => !s.IsPublished && s.IsActive),
            AdminStoreFilter.Suspended => stores.Where(s => !s.IsActive),
            _ => stores,
        };

        var page = new PageRequest(query.Page, query.PageSize);

        var sort = SortRequest.Parse(query.Sort, SortableFields, DefaultSortField);

        var rows = await Sort(stores, sort, db)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Slug,
                s.City,
                s.Country,
                s.Currency,
                s.IsPublished,
                s.IsActive,
                s.OwnerUserId,
                s.CreatedAt,
                ProductCount = db.Products.Count(p => p.StoreId == s.Id),
                OrderCount = db.Orders.Count(o => o.StoreId == s.Id),
            })
            .ToPagedResultAsync(page, cancellationToken);

        // One lookup for the page rather than one per row: owners live behind Identity, not in this
        // DbContext, so this cannot be a join.
        var owners = await users.GetByIdsAsync(
            rows.Items.Select(r => r.OwnerUserId).Distinct().ToArray(),
            cancellationToken);

        var items = rows.Items
            .Select(r =>
            {
                owners.TryGetValue(r.OwnerUserId, out var owner);

                return new AdminStoreListItemResponse(
                    r.Id,
                    r.Name,
                    r.Slug,
                    r.City,
                    r.Country,
                    r.Currency,
                    r.IsPublished,
                    r.IsActive,
                    // A store outliving its owner account is not supposed to happen, but a list page
                    // is the wrong place to discover that with an exception.
                    owner?.DisplayName ?? "Unknown",
                    owner?.Email ?? "—",
                    owner?.IsActive ?? false,
                    r.ProductCount,
                    r.OrderCount,
                    r.CreatedAt);
            })
            .ToList();

        return new PagedResult<AdminStoreListItemResponse>(items, rows.Page, rows.PageSize, rows.TotalCount);
    }

    private const string DefaultSortField = "createdAt";

    /// <summary>
    /// The only columns this endpoint sorts by. <see cref="SortRequest.Parse"/> rejects anything else
    /// before it reaches the query, so a stale bookmark renders the default list instead of failing.
    /// </summary>
    private static readonly string[] SortableFields =
        ["name", "createdAt", "orderCount", "productCount"];

    /// <summary>
    /// Every branch ends on <c>Id</c> so that rows which tie do not shuffle between pages — an
    /// unstable sort makes paging skip and repeat records.
    /// </summary>
    private static IQueryable<Store> Sort(IQueryable<Store> stores, SortRequest sort, IAppDbContext db) =>
        sort switch
        {
            { Field: "name", Descending: true } => stores.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            { Field: "name" } => stores.OrderBy(x => x.Name).ThenBy(x => x.Id),
            { Field: "orderCount", Descending: true } =>
                stores.OrderByDescending(x => db.Orders.Count(o => o.StoreId == x.Id)).ThenBy(x => x.Id),
            { Field: "orderCount" } =>
                stores.OrderBy(x => db.Orders.Count(o => o.StoreId == x.Id)).ThenBy(x => x.Id),
            { Field: "productCount", Descending: true } =>
                stores.OrderByDescending(x => db.Products.Count(p => p.StoreId == x.Id)).ThenBy(x => x.Id),
            { Field: "productCount" } =>
                stores.OrderBy(x => db.Products.Count(p => p.StoreId == x.Id)).ThenBy(x => x.Id),
            { Descending: false } => stores.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => stores.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
        };
}

/// <summary>
/// Suspends or restores a store.
///
/// Suspension is the platform's lever over a seller who is abusing it: the storefront stops being
/// reachable immediately, but the seller keeps their dashboard and nothing is destroyed, so it is
/// reversible in one click.
/// </summary>
public sealed class SetStoreActiveHandler(IAppDbContext db)
{
    public async Task HandleAsync(Guid storeId, SetActiveRequest request, CancellationToken cancellationToken)
    {
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken)
                    ?? throw new NotFoundException("Store", storeId);

        store.SetActive(request.IsActive);
        await db.SaveChangesAsync(cancellationToken);
    }
}
