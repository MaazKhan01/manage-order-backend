using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.CustomFields;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Orders;

/// <summary>
/// What a customer sees when they look up their own order.
///
/// Deliberately smaller than the seller's view. A customer gets what they need to know their order
/// is progressing and that we have the right details; they do not get the seller's private notes,
/// who inside the business touched it, or any platform identifier.
/// </summary>
public sealed record TrackOrderRequest(string Reference, string Phone);

public sealed record TrackedOrderAnswer(string Label, string? Value, bool IsPhoto);

public sealed record TrackedOrderItem(
    string ProductName,
    int Quantity,
    IReadOnlyList<TrackedOrderAnswer> Answers);

public sealed record TrackedOrderStep(string Status, DateTimeOffset At);

public sealed record TrackedOrderResponse(
    string Reference,
    string StoreName,
    string? StoreSlug,
    string? StoreWhatsApp,
    string Status,
    string PaymentStatus,
    decimal? TotalAmount,
    string Currency,
    DateOnly? DeliveryDate,
    string? DeliveryAddress,
    string? CustomerNote,
    string CustomerName,
    DateTimeOffset PlacedAt,
    DateTimeOffset LastUpdatedAt,
    IReadOnlyList<TrackedOrderItem> Items,
    IReadOnlyList<TrackedOrderStep> Timeline);

public sealed class TrackOrderValidator : AbstractValidator<TrackOrderRequest>
{
    public TrackOrderValidator()
    {
        RuleFor(x => x.Reference)
            .NotEmpty().WithMessage("Enter your order number.")
            .MaximumLength(32);

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Enter the phone number you ordered with.")
            .MaximumLength(32);
    }
}

/// <summary>
/// Looks an order up by its public reference plus the phone number it was placed with.
///
/// The phone is the whole authorisation model. A reference on its own is not a secret — it is
/// printed on a slip and read out over the phone — so knowing one must not be enough to read
/// somebody's name, address and order history.
///
/// Every failure returns the same "not found", whether the reference does not exist, the phone does
/// not match, or the store has been suspended. Distinguishing them would turn this into an oracle
/// for testing which references are real.
/// </summary>
public sealed class TrackOrderHandler(IAppDbContext db)
{
    public async Task<TrackedOrderResponse> HandleAsync(
        TrackOrderRequest request,
        CancellationToken cancellationToken)
    {
        var reference = OrderReference.Normalise(request.Reference);
        var phone = Customer.NormalisePhone(request.Phone);

        // Cheap rejection before touching the database: a malformed reference cannot match anything.
        if (!OrderReference.IsWellFormed(reference) || phone.Length < 6)
        {
            throw NotFound();
        }

        // Items and their answers are what the customer is checking, so they load with the order
        // rather than costing a second round trip.
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .ThenInclude(i => i.FieldValues)
            .FirstOrDefaultAsync(o => o.PublicReference == reference, cancellationToken)
            ?? throw NotFound();

        var customer = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == order.CustomerId)
            .Select(c => new { c.Name, c.Phone })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound();

        // The phone is normalised on both sides, so "0300 123 4567" and "03001234567" match.
        if (!string.Equals(customer.Phone, phone, StringComparison.Ordinal))
        {
            throw NotFound();
        }

        var store = await db.Stores
            .AsNoTracking()
            .Where(s => s.Id == order.StoreId)
            .Select(s => new { s.Name, s.Slug, s.WhatsApp, s.Currency, s.IsPublished, s.IsActive })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound();

        // A suspended store's orders stop being publicly readable along with its storefront.
        if (!store.IsActive)
        {
            throw NotFound();
        }

        var items = order.Items
            .Select(item => new TrackedOrderItem(
                item.ProductNameSnapshot,
                item.Quantity,
                item.FieldValues
                    .OrderBy(v => v.DisplayOrder)
                    .Select(v => new TrackedOrderAnswer(
                        v.LabelSnapshot,
                        // A photo's URL is not returned: the reference is guessable enough that a
                        // stored image should not become reachable through it.
                        v.FieldTypeSnapshot == CustomFieldType.Image ? null : Display(v),
                        v.FieldTypeSnapshot == CustomFieldType.Image))
                    .ToList()))
            .ToList();

        var timeline = await db.OrderStatusHistory
            .AsNoTracking()
            .Where(h => h.OrderId == order.Id)
            .OrderBy(h => h.CreatedAt)
            // Only what happened and when. `ChangedByUserId` and the seller's note stay behind.
            .Select(h => new TrackedOrderStep(h.ToStatus.ToString(), h.CreatedAt))
            .ToListAsync(cancellationToken);

        return new TrackedOrderResponse(
            order.PublicReference,
            store.Name,
            // Only linked when the customer could actually open it.
            store.IsPublished ? store.Slug : null,
            store.WhatsApp,
            order.Status.ToString(),
            order.PaymentStatus.ToString(),
            order.TotalAmount,
            store.Currency,
            order.DeliveryDate,
            order.DeliveryAddress,
            order.CustomerNote,
            customer.Name,
            order.CreatedAt,
            timeline.Count > 0 ? timeline[^1].At : order.UpdatedAt,
            items,
            timeline);
    }

    /// <summary>
    /// One message for every failure. The resource named is the reference the caller supplied, not
    /// anything we know about it.
    /// </summary>
    private static NotFoundException NotFound() => new("Order", "reference");

    private static string? Display(OrderFieldValue value) =>
        value.FieldTypeSnapshot switch
        {
            CustomFieldType.Number => value.ValueNumber?.ToString(),
            CustomFieldType.Date => value.ValueDate?.ToString("yyyy-MM-dd"),
            CustomFieldType.Time => value.ValueTime?.ToString("HH:mm"),
            CustomFieldType.Boolean => value.ValueBoolean is true ? "Yes" : "No",
            _ => value.ValueText,
        };
}
