using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Orders;

/// <summary>
/// A request a customer submitted from a storefront.
///
/// An order is a record of something that already happened, so it copies what it was made of rather
/// than pointing at live catalogue rows. Renaming a product or deleting a custom field must never
/// rewrite history. See docs/ADR/0004-order-snapshots.md.
/// </summary>
public sealed class Order : Entity, ITenantOwned
{
    private readonly List<OrderItem> _items = [];

    private Order() { }

    public Guid StoreId { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>
    /// Short, per-store, human-readable — "#1042". This is what the seller and customer say out loud.
    /// The Guid remains the API identifier, because a sequential number in a URL invites enumeration.
    /// </summary>
    public int OrderNumber { get; private set; }

    /// <summary>
    /// The reference a customer uses to look this order up - e.g. DM-2026-K4P7QX. Unique across the
    /// whole platform, because a customer types it into /track without saying which shop it was.
    /// Random rather than sequential; see <see cref="OrderReference"/>.
    /// </summary>
    public string PublicReference { get; private set; } = null!;

    public OrderStatus Status { get; private set; } = OrderStatus.New;

    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.Unpaid;

    /// <summary>
    /// Nullable: plenty of orders are quoted after the seller reads the details. V1 records money,
    /// it does not process it.
    /// </summary>
    public decimal? TotalAmount { get; private set; }

    public DateOnly? DeliveryDate { get; private set; }

    public string? DeliveryAddress { get; private set; }

    /// <summary>Anything the customer wanted to add that no field asked for.</summary>
    public string? CustomerNote { get; private set; }

    public OrderSource Source { get; private set; } = OrderSource.Storefront;

    /// <summary>
    /// Hashed, never raw. Enough to spot one address flooding a store; not a stored identifier for
    /// someone who never agreed to be tracked.
    /// </summary>
    public string? SubmittedFromIpHash { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    public static Order Create(
        Guid storeId,
        Guid customerId,
        int orderNumber,
        string publicReference,
        DateOnly? deliveryDate,
        string? deliveryAddress,
        string? customerNote,
        string? submittedFromIpHash)
    {
        if (orderNumber < 1)
        {
            throw new BusinessRuleException("An order number is required.");
        }

        if (!OrderReference.IsWellFormed(publicReference))
        {
            throw new BusinessRuleException("A valid public reference is required.");
        }

        return new Order
        {
            StoreId = storeId,
            CustomerId = customerId,
            OrderNumber = orderNumber,
            PublicReference = publicReference,
            DeliveryDate = deliveryDate,
            DeliveryAddress = Normalise(deliveryAddress),
            CustomerNote = Normalise(customerNote),
            SubmittedFromIpHash = submittedFromIpHash,
        };
    }

    /// <summary>
    /// Marks this as an order the seller recorded by hand rather than one a customer submitted.
    ///
    /// Kept as a domain method rather than a settable property so the source cannot be flipped after
    /// the fact: how an order arrived is a fact about its history, not a field to edit.
    /// </summary>
    public void MarkAsManual() => Source = OrderSource.Manual;

    public OrderItem AddItem(
        Guid? productId,
        string productNameSnapshot,
        decimal? unitPriceSnapshot,
        int quantity)
    {
        if (quantity < 1)
        {
            throw new BusinessRuleException("Quantity must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(productNameSnapshot))
        {
            throw new BusinessRuleException("A product name is required.");
        }

        var item = OrderItem.Create(Id, productId, productNameSnapshot, unitPriceSnapshot, quantity);
        _items.Add(item);

        RecalculateTotal();
        return item;
    }

    /// <summary>
    /// Sums the line totals. Null when nothing was priced — a total of zero would read as "free",
    /// which is a different and wrong statement.
    /// </summary>
    private void RecalculateTotal()
    {
        var priced = _items.Where(item => item.LineTotal is not null).ToList();
        TotalAmount = priced.Count == 0 ? null : priced.Sum(item => item.LineTotal!.Value);
    }

    public void ChangeStatus(OrderStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new BusinessRuleException($"'{status}' is not a valid status.");
        }

        if (!Status.CanTransitionTo(status))
        {
            throw new BusinessRuleException($"An order cannot go from {Status} to {status}.");
        }

        Status = status;
    }

    public void ChangePaymentStatus(PaymentStatus paymentStatus)
    {
        if (!Enum.IsDefined(paymentStatus))
        {
            throw new BusinessRuleException($"'{paymentStatus}' is not a valid payment status.");
        }

        PaymentStatus = paymentStatus;
    }

    public void SetTotal(decimal? total)
    {
        if (total is < 0)
        {
            throw new BusinessRuleException("A total cannot be negative.");
        }

        TotalAmount = total;
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OrderItem : Entity
{
    private readonly List<OrderFieldValue> _fieldValues = [];

    private OrderItem() { }

    public Guid OrderId { get; private set; }

    /// <summary>Nullable, and kept only for analytics. Rendering uses the snapshot.</summary>
    public Guid? ProductId { get; private set; }

    public string ProductNameSnapshot { get; private set; } = null!;

    public decimal? UnitPriceSnapshot { get; private set; }

    public int Quantity { get; private set; }

    public decimal? LineTotal { get; private set; }

    public IReadOnlyList<OrderFieldValue> FieldValues => _fieldValues;

    internal static OrderItem Create(
        Guid orderId,
        Guid? productId,
        string productNameSnapshot,
        decimal? unitPriceSnapshot,
        int quantity)
    {
        var item = new OrderItem
        {
            OrderId = orderId,
            ProductId = productId,
            ProductNameSnapshot = productNameSnapshot.Trim(),
            UnitPriceSnapshot = unitPriceSnapshot,
            Quantity = quantity,
        };

        item.LineTotal = unitPriceSnapshot is null ? null : unitPriceSnapshot * quantity;
        return item;
    }

    public void AddFieldValue(OrderFieldValue value) => _fieldValues.Add(value);
}
