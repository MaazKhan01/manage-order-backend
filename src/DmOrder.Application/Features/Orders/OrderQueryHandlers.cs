using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Application.Features.Catalogue;
using DmOrder.Domain.CustomFields;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Orders;

public sealed record OrderListQuery(
    int? Page,
    int? PageSize,
    OrderStatus? Status,
    string? Search,
    string? Sort);

public sealed class ListOrdersHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Columns this endpoint sorts by; anything else falls back to newest-first.</summary>
    private static readonly string[] SortableFields =
        ["createdAt", "orderNumber", "deliveryDate", "totalAmount"];

    public async Task<PagedResult<OrderListItemResponse>> HandleAsync(
        OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var orders = db.Orders.AsNoTracking().Where(o => o.StoreId == storeId);

        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);

            // Sellers search for a person or an order number, not a product. Matching the number
            // exactly avoids "12" pulling in every order containing that digit.
            var asNumber = int.TryParse(query.Search.Trim().TrimStart('#'), out var parsed) ? parsed : (int?)null;

            // Phones are stored in E.164 but searched for the way the seller knows them, so the
            // match is on trailing digits rather than a substring. Null when the term is too short
            // to be a number at all, in which case no phone match is attempted.
            var phone = SearchPattern.PhoneSuffix(query.Search);

            orders = orders.Where(o =>
                (asNumber != null && o.OrderNumber == asNumber)
                || db.Customers.Any(c => c.Id == o.CustomerId
                                         && (EF.Functions.Like(c.Name.ToLower(), pattern, SearchPattern.EscapeCharacter)
                                             || (phone != null && EF.Functions.Like(c.Phone, phone, SearchPattern.EscapeCharacter)))));
        }

        // Newest first by default: a seller opens this to see what just came in. Ties break on Id so
        // paging cannot skip or repeat an order.
        var sort = SortRequest.Parse(query.Sort, SortableFields, "createdAt");

        var ordered = sort switch
        {
            { Field: "orderNumber", Descending: true } => orders.OrderByDescending(o => o.OrderNumber).ThenBy(o => o.Id),
            { Field: "orderNumber" } => orders.OrderBy(o => o.OrderNumber).ThenBy(o => o.Id),
            { Field: "deliveryDate", Descending: true } => orders.OrderByDescending(o => o.DeliveryDate).ThenBy(o => o.Id),
            // Nulls last when sorting soonest-first: an order with no date is not "due today".
            { Field: "deliveryDate" } =>
                orders.OrderBy(o => o.DeliveryDate == null).ThenBy(o => o.DeliveryDate).ThenBy(o => o.Id),
            { Field: "totalAmount", Descending: true } => orders.OrderByDescending(o => o.TotalAmount).ThenBy(o => o.Id),
            { Field: "totalAmount" } => orders.OrderBy(o => o.TotalAmount).ThenBy(o => o.Id),
            { Descending: false } => orders.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id),
            _ => orders.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id),
        };
        var page = new PageRequest(query.Page, query.PageSize);

        var rows = await ordered
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.Status,
                o.PaymentStatus,
                o.TotalAmount,
                o.DeliveryDate,
                o.CreatedAt,
                CustomerName = db.Customers.Where(c => c.Id == o.CustomerId).Select(c => c.Name).FirstOrDefault(),
                CustomerPhone = db.Customers.Where(c => c.Id == o.CustomerId).Select(c => c.Phone).FirstOrDefault(),
                // The first product's name is enough for a list row; the detail view has the rest.
                FirstProduct = db.OrderItems
                    .Where(i => i.OrderId == o.Id)
                    .Select(i => i.ProductNameSnapshot)
                    .FirstOrDefault(),
                ItemCount = db.OrderItems.Count(i => i.OrderId == o.Id),
            })
            .ToPagedResultAsync(page, cancellationToken);

        var items = rows.Items
            .Select(r => new OrderListItemResponse(
                r.Id,
                r.OrderNumber,
                r.Status.ToString(),
                r.PaymentStatus.ToString(),
                r.TotalAmount,
                r.DeliveryDate,
                r.CreatedAt,
                r.CustomerName ?? "Unknown",
                r.CustomerPhone ?? string.Empty,
                r.ItemCount > 1
                    ? $"{r.FirstProduct} +{r.ItemCount - 1}"
                    : r.FirstProduct ?? string.Empty,
                r.ItemCount))
            .ToList();

        return new PagedResult<OrderListItemResponse>(items, rows.Page, rows.PageSize, rows.TotalCount);
    }
}

public sealed class GetOrderCountsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// One grouped query rather than six counts, so the filter tabs cost a single round trip.
    /// </summary>
    public async Task<OrderCountsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var counts = await db.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == storeId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        int For(OrderStatus status) => counts.TryGetValue(status, out var count) ? count : 0;

        return new OrderCountsResponse(
            For(OrderStatus.New),
            For(OrderStatus.Confirmed),
            For(OrderStatus.InProgress),
            For(OrderStatus.ReadyForDelivery),
            For(OrderStatus.Completed),
            For(OrderStatus.Cancelled),
            counts.Values.Sum());
    }
}

public sealed class GetOrderHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IUserDisplayNameLookup users)
{
    public async Task<OrderDetailResponse> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        // Scoped by store as well as id: an order belonging to another seller is simply not found.
        var order = await db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        var currency = await db.Stores
            .Where(s => s.Id == storeId)
            .Select(s => s.Currency)
            .FirstAsync(cancellationToken);

        var customer = await db.Customers
            .Where(c => c.Id == order.CustomerId)
            .Select(c => new { c.Id, c.Name, c.Phone, c.Email, c.AddressText })
            .FirstAsync(cancellationToken);

        var customerOrderCount = await db.Orders
            .CountAsync(o => o.CustomerId == order.CustomerId && o.StoreId == storeId, cancellationToken);

        // Reference photos are the only answers that need a URL, so only those media rows are loaded.
        var mediaIds = order.Items
            .SelectMany(i => i.FieldValues)
            .Where(v => v.MediaId is not null)
            .Select(v => v.MediaId!.Value)
            .ToArray();

        var mediaKeys = mediaIds.Length == 0
            ? []
            : await db.MediaAssets
                .Where(m => mediaIds.Contains(m.Id) && m.StoreId == storeId)
                .Select(m => new { m.Id, m.StorageKey })
                .ToDictionaryAsync(m => m.Id, m => m.StorageKey, cancellationToken);

        var history = await db.OrderStatusHistory
            .AsNoTracking()
            .Where(h => h.OrderId == order.Id)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);

        var notes = await db.OrderNotes
            .AsNoTracking()
            .Where(n => n.OrderId == order.Id)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        var userIds = history.Where(h => h.ChangedByUserId is not null).Select(h => h.ChangedByUserId!.Value)
            .Concat(notes.Select(n => n.AuthorUserId))
            .Distinct()
            .ToArray();

        var names = await users.GetDisplayNamesAsync(userIds, cancellationToken);

        return new OrderDetailResponse(
            order.Id,
            order.OrderNumber,
            order.PublicReference,
            order.Status.ToString(),
            order.PaymentStatus.ToString(),
            order.TotalAmount,
            currency,
            order.DeliveryDate,
            order.DeliveryAddress,
            order.CustomerNote,
            order.Source.ToString(),
            order.CreatedAt,
            order.Status.IsFinal(),
            new OrderCustomerResponse(
                customer.Id, customer.Name, customer.Phone, customer.Email, customer.AddressText, customerOrderCount),
            [.. order.Items.Select(item => new OrderItemResponse(
                item.Id,
                item.ProductId,
                item.ProductNameSnapshot,
                item.UnitPriceSnapshot,
                item.Quantity,
                item.LineTotal,
                [.. item.FieldValues
                    .OrderBy(v => v.DisplayOrder)
                    .Select(v => new OrderFieldValueResponse(
                        // The snapshot, always — never a lookup of the live field.
                        v.LabelSnapshot,
                        v.FieldTypeSnapshot.ToString(),
                        v.ValueText,
                        v.FieldTypeSnapshot == CustomFieldType.Image
                            && v.MediaId is { } id
                            && mediaKeys.TryGetValue(id, out var key)
                                ? storage.GetPublicUrl(key)
                                : null))]))],
            [.. history.Select(h => new OrderStatusHistoryResponse(
                h.FromStatus?.ToString(),
                h.ToStatus.ToString(),
                h.ChangedByUserId is { } uid && names.TryGetValue(uid, out var changedBy) ? changedBy : null,
                h.Note,
                h.CreatedAt))],
            [.. notes.Select(n => new OrderNoteResponse(
                n.Id,
                n.Body,
                names.TryGetValue(n.AuthorUserId, out var author) ? author : "Unknown",
                n.CreatedAt))]);
    }
}
