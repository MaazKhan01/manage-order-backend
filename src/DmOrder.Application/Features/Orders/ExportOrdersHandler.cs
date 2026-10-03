using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Application.Features.Catalogue;
using DmOrder.Domain.CustomFields;
using DmOrder.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Orders;

/// <summary>
/// The seller's orders, as a spreadsheet.
///
/// Sellers already keep a spreadsheet. Several of them told us so by asking for this: they reconcile
/// against it, hand it to an accountant, or sort it in ways no list screen will ever offer. Giving
/// them the data is cheaper than guessing which views they need, and it means the product never
/// becomes the only place their own orders exist.
///
/// The shape of the file is the point. One row per order, and **one column per question the seller
/// asks** - so "Size" and "Needed by" become sortable columns rather than text buried in a notes
/// field. That is what makes it a working document instead of a dump.
/// </summary>
public sealed record OrderExportRequest(OrderStatus? Status, string? Search);

/// <summary>
/// Rows and headers, ready to be written. Deliberately free of any spreadsheet type: the Application
/// layer decides what the data is, Infrastructure decides what a .xlsx looks like.
/// </summary>
public sealed record OrderExportData(
    string StoreName,
    string Currency,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

public sealed class ExportOrdersHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// A ceiling, so one request cannot try to materialise a hundred thousand rows into memory on a
    /// small instance. Far above what any seller on this product has, and low enough to stay safe.
    /// </summary>
    private const int MaxRows = 10_000;

    public async Task<OrderExportData> HandleAsync(
        OrderExportRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var store = await db.Stores
            .AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => new { s.Name, s.Currency })
            .FirstAsync(cancellationToken);

        /*
         * The question columns are built from the seller's current questions, in their display
         * order - not from whatever happens to appear in the orders.
         *
         * That keeps the columns stable between exports: a question nobody answered this month
         * still gets its column, so two files line up and can be pasted together. Answers are
         * matched by label rather than id, because the label is what was snapshotted onto the order
         * and what the seller recognises.
         */
        var questionLabels = await db.CustomFields
            .AsNoTracking()
            .Where(f => f.StoreId == storeId && f.DeletedAt == null)
            .OrderBy(f => f.DisplayOrder)
            .Select(f => f.Label)
            .ToListAsync(cancellationToken);

        var search = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : SearchPattern.Contains(request.Search);

        // Customer is joined rather than navigated: Order carries only CustomerId, which keeps the
        // aggregate boundary honest - a customer is a separate root that outlives any one order.
        var query = db.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == storeId)
            .Where(o => request.Status == null || o.Status == request.Status);

        if (search is not null)
        {
            query = query.Where(o =>
                EF.Functions.Like(o.PublicReference, search, SearchPattern.EscapeCharacter)
                || db.Customers.Any(c => c.Id == o.CustomerId
                                         && EF.Functions.Like(c.Name.ToLower(), search, SearchPattern.EscapeCharacter)));
        }

        var orders = await query
            .Include(o => o.Items)
                .ThenInclude(i => i.FieldValues)
            // Oldest first: a spreadsheet is read top to bottom as a history, which is the opposite
            // of the dashboard list where the newest order is the one that needs attention.
            .OrderBy(o => o.OrderNumber)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        // One lookup for every customer on the page rather than one per row.
        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var customers = await db.Customers
            .AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new ExportCustomer(c.Name, c.Phone, c.Email), cancellationToken);

        var headers = new List<string>
        {
            "Order", "Reference", "Placed", "Status", "Payment",
            "Customer", "Phone", "Email",
            "Product", "Quantity", $"Unit price ({store.Currency})", $"Total ({store.Currency})",
            "Needed by", "Delivery address", "Customer note", "Source",
        };
        headers.AddRange(questionLabels);

        var rows = orders.Select(order => BuildRow(order, customers, questionLabels)).ToList();

        return new OrderExportData(store.Name, store.Currency, headers, rows);
    }

    private sealed record ExportCustomer(string Name, string Phone, string? Email);

    private static IReadOnlyList<object?> BuildRow(
        Order order,
        IReadOnlyDictionary<Guid, ExportCustomer> customers,
        IReadOnlyList<string> questionLabels)
    {
        // One row per order. An order has one item in this product, so the first is the item - but
        // it is read defensively rather than indexed, because a row that throws loses the whole file.
        var item = order.Items.FirstOrDefault();
        customers.TryGetValue(order.CustomerId, out var customer);

        var row = new List<object?>
        {
            order.OrderNumber,
            order.PublicReference,
            // DateTime, not a formatted string: Excel can then sort and filter it as a date.
            order.CreatedAt.UtcDateTime,
            order.Status.ToString(),
            order.PaymentStatus.ToString(),
            customer?.Name,
            customer?.Phone,
            customer?.Email,
            item?.ProductNameSnapshot,
            item?.Quantity,
            item?.UnitPriceSnapshot,
            order.TotalAmount,
            order.DeliveryDate?.ToDateTime(TimeOnly.MinValue),
            order.DeliveryAddress,
            order.CustomerNote,
            order.Source.ToString(),
        };

        var answers = item?.FieldValues ?? [];

        foreach (var label in questionLabels)
        {
            var answer = answers.FirstOrDefault(
                a => string.Equals(a.LabelSnapshot, label, StringComparison.OrdinalIgnoreCase));

            row.Add(answer is null ? null : Readable(answer));
        }

        return row;
    }

    /// <summary>
    /// One answer as a spreadsheet cell.
    ///
    /// Numbers and dates stay typed so they sort correctly; everything else becomes text. An image
    /// answer becomes a note rather than a link: the media URL is only meaningful while the object
    /// exists, and a column of dead links a year from now is worse than a word.
    /// </summary>
    private static object? Readable(OrderFieldValue value) => value.FieldTypeSnapshot switch
    {
        CustomFieldType.Number => value.ValueNumber,
        CustomFieldType.Date => value.ValueDate?.ToDateTime(TimeOnly.MinValue),
        CustomFieldType.Time => value.ValueTime?.ToString("HH:mm"),
        CustomFieldType.Boolean => value.ValueBoolean is true ? "Yes" : value.ValueBoolean is false ? "No" : null,
        CustomFieldType.MultiSelect => JoinChoices(value.ValueJson),
        CustomFieldType.Image => value.MediaId is null ? null : "(photo attached)",
        _ => value.ValueText,
    };

    private static string? JoinChoices(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var choices = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
            return choices is { Count: > 0 } ? string.Join(", ", choices) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            // Stored by us, so this should not happen - but a malformed row must not cost the
            // seller their entire export.
            return null;
        }
    }
}
