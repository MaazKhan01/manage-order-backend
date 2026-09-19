namespace DmOrder.Domain.Orders;

/// <summary>
/// Where an order is in the seller's process.
///
/// Deliberately generic: "In progress" covers baking, sewing, arranging and shooting. A
/// trade-specific vocabulary here would be the first thing to break when the next kind of seller
/// signs up.
/// </summary>
public enum OrderStatus
{
    /// <summary>Just arrived. The seller has not looked at it yet.</summary>
    New = 0,

    /// <summary>The seller has accepted it and agreed the details.</summary>
    Confirmed = 1,

    /// <summary>Being made.</summary>
    InProgress = 2,

    /// <summary>Finished and waiting to be collected or delivered.</summary>
    ReadyForDelivery = 3,

    /// <summary>Handed over. The end of the happy path.</summary>
    Completed = 4,

    /// <summary>Called off, by either side.</summary>
    Cancelled = 5,
}

public enum PaymentStatus
{
    Unpaid = 0,

    /// <summary>A deposit has been taken — how most custom work actually starts.</summary>
    PartiallyPaid = 1,

    Paid = 2,
    Refunded = 3,
}

public enum OrderSource
{
    /// <summary>Submitted through the seller's public page.</summary>
    Storefront = 0,

    /// <summary>Entered by the seller on behalf of a customer who messaged them. Phase 6.</summary>
    Manual = 1,
}

public static class OrderStatusRules
{
    /// <summary>
    /// Which moves are allowed.
    ///
    /// Forward steps may skip ahead — a seller who finishes a small order in an hour should not have
    /// to click through every stage. Going backwards is allowed only while the work is still open,
    /// because a completed or cancelled order is a closed record, and reopening it silently would let
    /// history be rewritten.
    /// </summary>
    public static bool CanTransitionTo(this OrderStatus current, OrderStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            OrderStatus.Completed => false,
            OrderStatus.Cancelled => false,
            _ => true,
        };
    }

    /// <summary>A status the seller can no longer move away from.</summary>
    public static bool IsFinal(this OrderStatus status) =>
        status is OrderStatus.Completed or OrderStatus.Cancelled;
}
