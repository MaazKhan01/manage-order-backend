using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Application.Features.Catalogue;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Orders;

public sealed record CustomerListQuery(int? Page, int? PageSize, string? Search, string? Sort);

public sealed class ListCustomersHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Columns this endpoint sorts by; anything else falls back to the default.</summary>
    private static readonly string[] SortableFields = ["name", "orderCount", "lastOrderAt"];

    public async Task<PagedResult<CustomerListItemResponse>> HandleAsync(
        CustomerListQuery query,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var customers = db.Customers.AsNoTracking().Where(c => c.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);

            // See the note in ListOrdersHandler: E.164 storage means a phone is matched on its
            // trailing digits, not as a substring of what the seller typed.
            var phone = SearchPattern.PhoneSuffix(query.Search);

            customers = customers.Where(c =>
                EF.Functions.Like(c.Name.ToLower(), pattern, SearchPattern.EscapeCharacter)
                || (phone != null && EF.Functions.Like(c.Phone, phone, SearchPattern.EscapeCharacter)));
        }

        var page = new PageRequest(query.Page, query.PageSize);

        // Aggregates are computed in the query rather than by loading orders. A seller with a few
        // hundred customers should not pull their whole order history to render a list.
        var sort = SortRequest.Parse(query.Sort, SortableFields, "name", defaultDescending: false);

        // Ties break on Id so paging cannot skip or repeat a customer.
        var sorted = sort switch
        {
            { Field: "name", Descending: true } => customers.OrderByDescending(c => c.Name).ThenBy(c => c.Id),
            { Field: "orderCount", Descending: true } =>
                customers.OrderByDescending(c => db.Orders.Count(o => o.CustomerId == c.Id)).ThenBy(c => c.Id),
            { Field: "orderCount" } =>
                customers.OrderBy(c => db.Orders.Count(o => o.CustomerId == c.Id)).ThenBy(c => c.Id),
            { Field: "lastOrderAt", Descending: true } =>
                customers
                    .OrderByDescending(c => db.Orders.Where(o => o.CustomerId == c.Id).Max(o => (DateTimeOffset?)o.CreatedAt))
                    .ThenBy(c => c.Id),
            { Field: "lastOrderAt" } =>
                customers
                    .OrderBy(c => db.Orders.Where(o => o.CustomerId == c.Id).Max(o => (DateTimeOffset?)o.CreatedAt))
                    .ThenBy(c => c.Id),
            _ => customers.OrderBy(c => c.Name).ThenBy(c => c.Id),
        };

        var rows = await sorted
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Phone,
                c.Email,
                OrderCount = db.Orders.Count(o => o.CustomerId == c.Id),
                LastOrderAt = db.Orders
                    .Where(o => o.CustomerId == c.Id)
                    .Max(o => (DateTimeOffset?)o.CreatedAt),
                // Only completed orders count as money taken. Counting cancelled ones would overstate
                // what a customer is worth.
                TotalSpent = db.Orders
                    .Where(o => o.CustomerId == c.Id && o.Status == OrderStatus.Completed)
                    .Sum(o => (decimal?)o.TotalAmount),
                // Needed because the sum cannot express "nothing completed yet": EF translates a
                // nullable Sum to COALESCE(SUM(...), 0), so an empty set comes back as 0 and is
                // indistinguishable from a completed order that was never priced.
                CompletedCount = db.Orders
                    .Count(o => o.CustomerId == c.Id && o.Status == OrderStatus.Completed),
            })
            .ToPagedResultAsync(page, cancellationToken);

        var items = rows.Items
            .Select(r => new CustomerListItemResponse(
                r.Id,
                r.Name,
                r.Phone,
                r.Email,
                r.OrderCount,
                r.LastOrderAt,
                r.CompletedCount == 0 ? null : r.TotalSpent))
            .ToList();

        return new PagedResult<CustomerListItemResponse>(items, rows.Page, rows.PageSize, rows.TotalCount);
    }
}

public sealed class GetCustomerHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<CustomerDetailResponse> HandleAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        // Customers are per store, so this scoping is what keeps one seller's customer list entirely
        // invisible to another.
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId && c.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Customer", customerId);

        var currency = await db.Stores
            .Where(s => s.Id == storeId)
            .Select(s => s.Currency)
            .FirstAsync(cancellationToken);

        var orders = await db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.StoreId == storeId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new CustomerOrderSummaryResponse(
                o.Id, o.OrderNumber, o.Status.ToString(), o.TotalAmount, o.CreatedAt))
            .ToListAsync(cancellationToken);

        var totalSpent = await db.Orders
            .Where(o => o.CustomerId == customerId
                        && o.StoreId == storeId
                        && o.Status == OrderStatus.Completed)
            .SumAsync(o => (decimal?)o.TotalAmount, cancellationToken);

        return new CustomerDetailResponse(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.AddressText,
            currency,
            customer.CreatedAt,
            orders.Count,
            totalSpent,
            orders);
    }
}
