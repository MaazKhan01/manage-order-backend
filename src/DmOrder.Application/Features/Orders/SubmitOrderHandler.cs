using DmOrder.Application.Common.Exceptions;
using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Catalogue;
using DmOrder.Domain.CustomFields;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;
using DmOrder.Domain.Stores;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Orders;

/// <summary>
/// The anonymous order submission.
///
/// This is the most hostile surface in the product: no authentication, a public URL, and it writes to
/// the database. Everything it touches is derived from the store slug and the seller's own
/// configuration — never from ids the caller supplies, beyond the field ids it validates against the
/// seller's list.
/// </summary>
public sealed class SubmitOrderHandler(
    IAppDbContext db,
    ILogger<SubmitOrderHandler> logger)
{
    public async Task<SubmitOrderResponse> HandleAsync(
        string storeSlug,
        SubmitOrderRequest request,
        string? clientIpHash,
        CancellationToken cancellationToken)
    {
        var slug = storeSlug.Trim().ToLowerInvariant();

        var store = await db.Stores
            .FirstOrDefaultAsync(s => s.Slug == slug && s.IsPublished && s.IsActive, cancellationToken)
            ?? throw new NotFoundException("Store", slug);

        // Honeypot. Answered with a plausible success so a bot has nothing to learn and no reason to
        // retry with a different shape.
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            logger.LogInformation("Discarded a honeypot submission for store {StoreId}", store.Id);
            return new SubmitOrderResponse(0, store.Name, store.WhatsApp);
        }

        var product = await db.Products
            .FirstOrDefaultAsync(
                p => p.StoreId == store.Id
                     && p.Slug == CatalogueSlug.Normalise(request.ProductSlug)
                     && p.DeletedAt == null
                     && p.IsActive,
                cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductSlug);

        if (!product.AcceptsCustomOrder)
        {
            throw new BusinessRuleException("This item is not available to order right now.");
        }

        // The seller's questions for this product, plus their store-wide ones.
        var fields = await db.CustomFields
            .Where(f => f.StoreId == store.Id
                        && f.DeletedAt == null
                        && (f.ProductId == product.Id || f.ProductId == null))
            .OrderBy(f => f.DisplayOrder)
            .ToListAsync(cancellationToken);

        var validation = CustomFieldValidator.Validate(
            fields,
            [.. request.Answers.Select(a => new CustomFieldAnswer(a.FieldId, a.Value, a.Values, a.MediaId))]);

        if (!validation.IsValid)
        {
            // Reported per field so the form can mark each one, rather than making the customer guess.
            throw new RequestValidationException(
                validation.Errors
                    .GroupBy(e => e.FieldId.ToString())
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));
        }

        await RequireAnswerMediaBelongsToStoreAsync(db, store.Id, validation.Answers, cancellationToken);

        var customer = await UpsertCustomerAsync(db, store.Id, request, cancellationToken);
        var order = await CreateOrderAsync(db, store, customer, product, request, validation, clientIpHash, cancellationToken);

        logger.LogInformation(
            "Order {OrderNumber} submitted to store {StoreId}", order.OrderNumber, store.Id);

        return new SubmitOrderResponse(order.OrderNumber, store.Name, store.WhatsApp);
    }

    /// <summary>
    /// Reference images are uploaded anonymously before the order is submitted, so the media id in
    /// the payload is attacker-controlled. Without this a submission could attach another store's
    /// upload — or another customer's — to an order.
    /// </summary>
    private static async Task RequireAnswerMediaBelongsToStoreAsync(
        IAppDbContext db,
        Guid storeId,
        IReadOnlyList<ValidatedAnswer> answers,
        CancellationToken cancellationToken)
    {
        var mediaIds = answers.Where(a => a.MediaId is not null).Select(a => a.MediaId!.Value).ToArray();

        if (mediaIds.Length == 0)
        {
            return;
        }

        var found = await db.MediaAssets
            .CountAsync(m => mediaIds.Contains(m.Id) && m.StoreId == storeId, cancellationToken);

        if (found != mediaIds.Length)
        {
            throw new BusinessRuleException("One of the uploaded photos could not be found. Please try again.");
        }
    }

    private static async Task<Customer> UpsertCustomerAsync(
        IAppDbContext db,
        Guid storeId,
        SubmitOrderRequest request,
        CancellationToken cancellationToken)
    {
        var phone = Customer.NormalisePhone(request.CustomerPhone);

        var existing = await db.Customers
            .FirstOrDefaultAsync(c => c.StoreId == storeId && c.Phone == phone, cancellationToken);

        if (existing is not null)
        {
            // A returning customer's record is refreshed, not replaced — blanks never erase what they
            // told the seller last time.
            existing.UpdateFromOrder(request.CustomerName, request.CustomerEmail, request.DeliveryAddress);
            return existing;
        }

        var customer = Customer.Create(
            storeId, request.CustomerName, request.CustomerPhone, request.CustomerEmail, request.DeliveryAddress);

        db.Customers.Add(customer);
        return customer;
    }

    private async Task<Order> CreateOrderAsync(
        IAppDbContext db,
        Store store,
        Customer customer,
        Product product,
        SubmitOrderRequest request,
        CustomFieldValidationResult validation,
        string? clientIpHash,
        CancellationToken cancellationToken)
    {
        // Retried because two customers can order in the same instant and land on the same number.
        // The unique index rejects the loser, and taking the next number is the right answer rather
        // than failing a real order.
        const int maxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var nextNumber = await db.Orders
                .Where(o => o.StoreId == store.Id)
                .Select(o => (int?)o.OrderNumber)
                .MaxAsync(cancellationToken) ?? 0;

            var order = Order.Create(
                store.Id,
                customer.Id,
                nextNumber + 1,
                ExtractDeliveryDate(validation),
                request.DeliveryAddress,
                request.CustomerNote,
                clientIpHash);

            var item = order.AddItem(
                product.Id,
                // Snapshotted: renaming or deleting the product later must not rewrite this order.
                product.Name,
                product.Price,
                request.Quantity);

            var displayOrder = 0;
            foreach (var answer in validation.Answers)
            {
                item.AddFieldValue(BuildFieldValue(item.Id, answer, displayOrder++));
            }

            db.Orders.Add(order);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return order;
            }
            catch (DbUpdateException exception)
                when (attempt < maxAttempts && IsUniqueViolation(exception))
            {
                logger.LogInformation(
                    "Order number collided for store {StoreId}; retrying (attempt {Attempt})",
                    store.Id, attempt);
            }
        }
    }

    /// <summary>
    /// Lifts a delivery date out of the seller's own questions onto the order.
    ///
    /// Sellers overwhelmingly ask for one, and it is the field their whole week is organised around,
    /// so it is worth having as a column to sort and filter by rather than only inside an answer.
    /// </summary>
    private static DateOnly? ExtractDeliveryDate(CustomFieldValidationResult validation) =>
        validation.Answers
            .Where(a => a.Field.FieldType == CustomFieldType.Date && a.Date is not null)
            .OrderBy(a => a.Field.DisplayOrder)
            .Select(a => a.Date)
            .FirstOrDefault();

    private static OrderFieldValue BuildFieldValue(Guid orderItemId, ValidatedAnswer answer, int displayOrder)
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

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is System.Data.Common.DbException { SqlState: "23505" };
}

public sealed class SubmitOrderValidator : AbstractValidator<SubmitOrderRequest>
{
    public SubmitOrderValidator()
    {
        RuleFor(x => x.ProductSlug).NotEmpty().MaximumLength(80);

        RuleFor(x => x.Quantity)
            .InclusiveBetween(1, 999)
            .WithMessage("Choose a quantity between 1 and 999.");

        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("Please give your name.")
            .MaximumLength(160);

        RuleFor(x => x.CustomerPhone)
            .NotEmpty().WithMessage("Please give a phone number.")
            .MaximumLength(32)
            .Must(HaveEnoughDigits).WithMessage("Enter a valid phone number.");

        RuleFor(x => x.CustomerEmail)
            .MaximumLength(256)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail))
            .WithMessage("Enter a valid email address.");

        RuleFor(x => x.DeliveryAddress).MaximumLength(500);
        RuleFor(x => x.CustomerNote).MaximumLength(2000);

        // A cap on the payload itself, independent of the request size limit. Answers beyond the
        // seller's field count are ignored anyway, so a huge list is only ever an attack.
        RuleFor(x => x.Answers)
            .NotNull()
            .Must(answers => answers.Count <= 100)
            .WithMessage("Too many answers were submitted.");
    }

    /// <summary>
    /// Deliberately loose: sellers here take orders from several countries and formats, and a strict
    /// pattern would reject real customers. It only rules out obvious nonsense.
    /// </summary>
    private static bool HaveEnoughDigits(string? phone) =>
        phone is not null && phone.Count(char.IsAsciiDigit) is >= 7 and <= 20;
}
