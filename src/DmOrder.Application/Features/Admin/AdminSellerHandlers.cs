using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Admin;

public sealed record AdminSellerListQuery(int? Page, int? PageSize, string? Search, bool? IsActive, string? Sort);

/// <summary>
/// Seller accounts, with the store each one owns.
///
/// The store list answers "what is on the platform"; this answers "who is on it", which is the view
/// that matters when someone has signed up but never finished setting a store up.
/// </summary>
public sealed class ListAdminSellersHandler(IAppDbContext db, IUserDirectory users)
{
    /// <summary>The only columns this endpoint sorts by; see <see cref="SortRequest.Parse"/>.</summary>
    private static readonly string[] SortableFields = ["displayName", "email", "createdAt"];

    public async Task<PagedResult<AdminSellerListItemResponse>> HandleAsync(
        AdminSellerListQuery query,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest(query.Page, query.PageSize);

        // Paging happens over accounts, because an account is the row. The store is a decoration on
        // it, and a seller may not have one at all.
        var sellers = await users.ListSellersAsync(
            page,
            query.Search,
            query.IsActive,
            SortRequest.Parse(query.Sort, SortableFields, "createdAt"),
            cancellationToken);

        var ownerIds = sellers.Items.Select(s => s.Id).ToArray();

        var stores = await db.Stores
            .AsNoTracking()
            .Where(s => ownerIds.Contains(s.OwnerUserId))
            .Select(s => new
            {
                s.OwnerUserId,
                s.Name,
                s.Slug,
                s.IsPublished,
                OrderCount = db.Orders.Count(o => o.StoreId == s.Id),
            })
            .ToListAsync(cancellationToken);

        // One store per seller in V1, but keyed by owner rather than assumed, so this keeps working
        // the day that stops being true.
        var storesByOwner = stores
            .GroupBy(s => s.OwnerUserId)
            .ToDictionary(g => g.Key, g => g.First());

        var items = sellers.Items
            .Select(seller =>
            {
                storesByOwner.TryGetValue(seller.Id, out var store);

                return new AdminSellerListItemResponse(
                    seller.Id,
                    seller.DisplayName,
                    seller.Email,
                    seller.IsActive,
                    store?.Name,
                    store?.Slug,
                    store?.IsPublished,
                    store?.OrderCount ?? 0,
                    seller.CreatedAt);
            })
            .ToList();

        return new PagedResult<AdminSellerListItemResponse>(
            items, sellers.Page, sellers.PageSize, sellers.TotalCount);
    }
}

/// <summary>
/// Suspends or restores a seller account.
///
/// Heavier than suspending a store: the person cannot sign in at all, and their existing session dies
/// at the next refresh. Their data is untouched, and restoring is one click.
/// </summary>
public sealed class SetSellerActiveHandler(IUserDirectory users)
{
    public Task HandleAsync(Guid userId, SetActiveRequest request, CancellationToken cancellationToken) =>
        // The directory refuses anything that is not a Seller, so this route cannot be turned on an
        // administrator — including the caller.
        users.SetSellerActiveAsync(userId, request.IsActive, cancellationToken);
}
