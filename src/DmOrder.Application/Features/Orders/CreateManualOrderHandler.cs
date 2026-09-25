using DmOrder.Application.Common.Exceptions;
using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.Catalogue;
using DmOrder.Domain.CustomFields;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Orders;

/// <summary>
/// An order the seller is recording on someone else's behalf.
///
/// Most of these sellers still take orders the old way — a DM, a WhatsApp voice note, a conversation
/// at a stall — and a dashboard that can only see orders placed through its own form shows them half
/// their business. This is what makes the order list the whole picture rather than a channel report.
/// </summary>
public sealed record ManualOrderRequest(
    /// <summary>A catalogue product, or null when the seller is recording a one-off.</summary>
    Guid? ProductId,
    /// <summary>Required when <see cref="ProductId"/> is null. Free text, snapshotted like any other.</summary>
    string? ProductName,
    /// <summary>Null means "not priced yet", which is a real state for custom work.</summary>
    decimal? UnitPrice,
    int Quantity,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string? DeliveryAddress,
    DateOnly? DeliveryDate,
    string? CustomerNote,
    IReadOnlyList<ManualOrderAnswer> Answers);

public sealed record ManualOrderAnswer(Guid FieldId, string? Value, List<string>? Values, Guid? MediaId);

public sealed class ManualOrderValidator : AbstractValidator<ManualOrderRequest>
{
    public ManualOrderValidator(IPhoneNumbers phones)
    {
        RuleFor(x => x.Quantity)
            .InclusiveBetween(1, 999)
            .WithMessage("Choose a quantity between 1 and 999.");

        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("Whose order is this?")
            .MaximumLength(160);

        // Required even here. The phone is how a returning customer is matched to their existing
        // record, and how they track the order afterwards - an order without one is a dead end.
        RuleFor(x => x.CustomerPhone)
            .NotEmpty().WithMessage("A phone number is needed so the customer can track this order.")
            .MaximumLength(32)
            .Must(phone => phones.Parse(phone).IsValid)
            .WithMessage("Enter a valid phone number, including the country code.");

        RuleFor(x => x.CustomerEmail)
            .MaximumLength(256)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail))
            .WithMessage("Enter a valid email address.");

        RuleFor(x => x.ProductName)
            .NotEmpty()
            .When(x => x.ProductId is null)
            .WithMessage("Choose a product, or give this one a name.");

        RuleFor(x => x.ProductName).MaximumLength(200);

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.UnitPrice.HasValue)
            .WithMessage("A price cannot be negative.");

        RuleFor(x => x.DeliveryAddress).MaximumLength(500);
        RuleFor(x => x.CustomerNote).MaximumLength(2000);

        RuleFor(x => x.Answers)
            .NotNull()
            .Must(answers => answers.Count <= 100)
            .WithMessage("Too many answers were submitted.");
    }
}

public sealed class CreateManualOrderHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IOrderReferenceFactory references,
    IPhoneNumbers phones)
{
    public async Task<ManualOrderCreatedResponse> HandleAsync(
        ManualOrderRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        // A product id from the request is untrusted like any other: it is checked against this
        // store, so a seller cannot attach another seller's product to their own order.
        var product = request.ProductId is { } productId
            ? await db.Products.FirstOrDefaultAsync(
                  p => p.Id == productId && p.StoreId == storeId && p.DeletedAt == null,
                  cancellationToken)
              ?? throw new NotFoundException("Product", productId)
            : null;

        var fields = product is null
            ? []
            : await db.CustomFields
                .AsNoTracking()
                .Where(f => f.StoreId == storeId
                            && f.DeletedAt == null
                            && (f.ProductId == null || f.ProductId == product.Id))
                .Include(f => f.Options)
                .ToListAsync(cancellationToken);

        // Answers are validated when given, but nothing is *required*.
        //
        // The seller is transcribing a conversation that already happened. Refusing to record a real
        // order because the customer never mentioned a field the form would have insisted on would
        // make this feature useless for the case it exists to serve — so required-ness is relaxed
        // and correctness is not.
        //
        // No lower bound on dates either: a manual order is often recorded after the fact.
        var answered = request.Answers.Where(a => HasValue(a)).ToList();

        var validation = CustomFieldValidator.Validate(
            [.. fields.Where(f => answered.Any(a => a.FieldId == f.Id))],
            [.. answered.Select(a => new CustomFieldAnswer(a.FieldId, a.Value, a.Values, a.MediaId))]);

        if (!validation.IsValid)
        {
            throw new RequestValidationException(validation.Errors.ToDictionary(
                e => e.FieldId.ToString(),
                e => new[] { e.Message }));
        }

        var customer = await UpsertCustomerAsync(storeId, request, cancellationToken);

        var order = await CreateOrderAsync(storeId, customer, product, request, validation, cancellationToken);

        return new ManualOrderCreatedResponse(order.Id, order.OrderNumber, order.PublicReference);
    }

    private static bool HasValue(ManualOrderAnswer answer) =>
        !string.IsNullOrWhiteSpace(answer.Value)
        || answer.Values is { Count: > 0 }
        || answer.MediaId is not null;

    private async Task<Customer> UpsertCustomerAsync(
        Guid storeId,
        ManualOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Matched on the canonical number, so the same person recorded by hand and by the public
        // form is one customer rather than two.
        var phone = phones.Normalise(request.CustomerPhone);

        var existing = await db.Customers
            .FirstOrDefaultAsync(c => c.StoreId == storeId && c.Phone == phone, cancellationToken);

        if (existing is not null)
        {
            existing.UpdateFromOrder(request.CustomerName, request.CustomerEmail, request.DeliveryAddress);
            return existing;
        }

        var customer = Customer.Create(
            storeId, request.CustomerName, phone, request.CustomerEmail, request.DeliveryAddress);

        db.Customers.Add(customer);
        return customer;
    }

    private async Task<Order> CreateOrderAsync(
        Guid storeId,
        Customer customer,
        Domain.Catalogue.Product? product,
        ManualOrderRequest request,
        CustomFieldValidationResult validation,
        CancellationToken cancellationToken)
    {
        // Same retry as the public path: the per-store number and the public reference each have a
        // unique index, and losing a race is a reason to try again rather than to fail a real order.
        const int maxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var nextNumber = await db.Orders
                .Where(o => o.StoreId == storeId)
                .Select(o => (int?)o.OrderNumber)
                .MaxAsync(cancellationToken) ?? 0;

            var order = Order.Create(
                storeId,
                customer.Id,
                nextNumber + 1,
                references.Next(),
                request.DeliveryDate,
                request.DeliveryAddress,
                request.CustomerNote,
                // No IP: nobody submitted this over the internet. Recording the seller's own address
                // would be meaningless and is customer data we have no reason to hold.
                submittedFromIpHash: null);

            order.MarkAsManual();

            var item = order.AddItem(
                product?.Id,
                // Snapshotted exactly like a storefront order, so a one-off product name survives
                // and a catalogue rename never rewrites history.
                product?.Name ?? request.ProductName!.Trim(),
                request.UnitPrice ?? product?.Price,
                request.Quantity);

            var displayOrder = 0;
            foreach (var answer in validation.Answers)
            {
                item.AddFieldValue(ManualFieldValue(item.Id, answer, displayOrder++));
            }

            db.Orders.Add(order);

            // The timeline starts here, recording the seller as the actor - unlike a storefront
            // order, a person really did do this.
            db.OrderStatusHistory.Add(OrderStatusHistory.Record(
                order.Id,
                fromStatus: null,
                order.Status,
                changedByUserId: currentUser.RequireUserId(),
                note: "Added by hand"));

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return order;
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                // Retried with a fresh number and reference.
            }
        }
    }

    private static OrderFieldValue ManualFieldValue(Guid orderItemId, ValidatedAnswer answer, int displayOrder)
    {
        var value = OrderFieldValue.Create(orderItemId, answer.Field, displayOrder);

        return answer.Field.FieldType switch
        {
            CustomFieldType.Number => value.WithNumber(answer.Number),
            CustomFieldType.Date => value.WithDate(answer.Date),
            CustomFieldType.Time => value.WithTime(answer.Time),
            CustomFieldType.Boolean => value.WithBoolean(answer.Boolean),
            CustomFieldType.MultiSelect => value.WithChoices(answer.Choices ?? []),
            CustomFieldType.Image => value.WithMedia(answer.MediaId),
            _ => value.WithText(answer.Text),
        };
    }
}

public sealed record ManualOrderCreatedResponse(Guid Id, int OrderNumber, string Reference);
