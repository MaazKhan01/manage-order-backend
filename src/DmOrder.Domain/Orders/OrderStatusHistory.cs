using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Orders;

/// <summary>
/// Append-only record of every status change.
///
/// Exists because "when did you mark this ready?" is a real question between a seller and a customer,
/// and because a status field alone loses the answer the moment it is overwritten. Nothing edits or
/// deletes a row here.
/// </summary>
public sealed class OrderStatusHistory : Entity
{
    private OrderStatusHistory() { }

    public Guid OrderId { get; private set; }

    /// <summary>Null for the row written when the order first arrives.</summary>
    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    /// <summary>Null when the customer's submission created it, rather than a seller acting.</summary>
    public Guid? ChangedByUserId { get; private set; }

    public string? Note { get; private set; }

    public static OrderStatusHistory Record(
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        Guid? changedByUserId,
        string? note) =>
        new()
        {
            OrderId = orderId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ChangedByUserId = changedByUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
}

/// <summary>
/// A seller's private note on an order.
///
/// **Never returned by anything under /api/v1/public.** This is where a seller writes "customer was
/// difficult last time" or "needs chasing about payment", and it must not be one mistaken join away
/// from a storefront response.
/// </summary>
public sealed class OrderNote : Entity
{
    private OrderNote() { }

    public Guid OrderId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Body { get; private set; } = null!;

    public static OrderNote Create(Guid orderId, Guid authorUserId, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new BusinessRuleException("A note cannot be empty.");
        }

        return new OrderNote
        {
            OrderId = orderId,
            AuthorUserId = authorUserId,
            Body = body.Trim(),
        };
    }
}
