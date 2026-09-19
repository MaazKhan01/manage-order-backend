using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Application.Features.Catalogue;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Orders;

public sealed record CustomerListQuery(int? Page, int? PageSize, string? Search);

public sealed class ListCustomersHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<PagedResult<CustomerListItemResponse>> HandleAsync(
        CustomerListQuery query,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var customers = db.Customers.AsNoTracking().Where(c => c.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = EscapeLikeWildcards(query.Search.Trim().ToLowerInvariant());
            customers = customers.Where(c =>
                EF.Functions.Like(c.Name.ToLower(), $"%{term}%", "\\")
                || EF.Functions.Like(c.Phone, $"%{term}%", "\\"));
        }

        var page = new PageRequest(query.Page, query.PageSize);

        // Aggregates are computed in the query rather than by loading orders. A seller with a few
        // hundred customers should not pull their whole order history to render a list.
        var rows = await customers
            .OrderBy(c => c.Name)
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
            })
            .ToPagedResultAsync(page, cancellationToken);

        var items = rows.Items
            .Select(r => new CustomerListItemResponse(
                r.Id, r.Name, r.Phone, r.Email, r.OrderCount, r.LastOrderAt, r.TotalSpent))
            .ToList();

        return new PagedResult<CustomerListItemResponse>(items, rows.Page, rows.PageSize, rows.TotalCount);
    }

    private static string EscapeLikeWildcards(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
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
