namespace DmOrder.Domain.Billing;

/// <summary>
/// Where a store stands commercially.
///
/// The backend is the source of truth for every one of these. The frontend renders what it is told
/// and enforces nothing — a locked button is a courtesy, not a control.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>Inside the free month of order management. The default for a new store.</summary>
    Trialing = 0,

    /// <summary>Paid and current.</summary>
    Active = 1,

    /// <summary>
    /// The free month ran out and nothing was bought. The storefront is untouched and still public;
    /// only the management dashboard is locked.
    /// </summary>
    TrialExpired = 2,

    /// <summary>A payment failed. Same effect as an expired trial, different thing to say about it.</summary>
    PastDue = 3,

    /// <summary>Ended, by the seller or the provider. Their data is kept.</summary>
    Cancelled = 4,
}
