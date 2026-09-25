using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Billing;
using DmOrder.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Billing;

/// <summary>
/// Raised when a seller reaches for something their plan does not currently include.
///
/// Separate from <see cref="ForbiddenException"/> on purpose. "You are not allowed to do this" and
/// "this needs a subscription" are different answers with different remedies, and the frontend has
/// to be able to tell them apart to show the right screen. Maps to 402.
/// </summary>
public sealed class PaymentRequiredException(string message, string reason)
    : DomainException(message)
{
    /// <summary>A stable machine-readable code, so the UI branches on this rather than on prose.</summary>
    public string Reason { get; } = reason;
}

public static class PaymentRequiredReasons
{
    public const string TrialExpired = "trial_expired";
    public const string SubscriptionInactive = "subscription_inactive";
    public const string FreeProductLimitReached = "free_product_limit_reached";
}

/// <summary>
/// The single place the paid/free line is enforced.
///
/// **This is the enforcement.** The dashboard also greys buttons out, but that is a courtesy — a
/// locked UI with an open API is not a paywall, so every management handler goes through here.
///
/// Two rules, and only two:
///
///   • Order management needs an unlocked subscription.
///   • Publishing beyond the free product limit needs a paid one.
///
/// Everything else — the storefront, the catalogue, the theme, and accepting new orders from the
/// public — stays free forever and must never be routed through this class.
/// </summary>
public sealed class SubscriptionGuard(IAppDbContext db, IDateTimeProvider clock, IPlanPolicy plan)
{
    /// <summary>
    /// The store's subscription, creating nothing. Missing means a store predating billing, which is
    /// treated as unlocked rather than locked — failing open here is right, because the alternative
    /// is locking a paying-era seller out over a data gap.
    /// </summary>
    public async Task<Subscription?> FindAsync(Guid storeId, CancellationToken cancellationToken) =>
        await db.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, cancellationToken);

    /// <summary>
    /// Throws unless the store may use order management right now.
    ///
    /// Called by every seller-side order and customer handler.
    /// </summary>
    public async Task RequireManagementAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var subscription = await FindAsync(storeId, cancellationToken);

        // No row: a store created before subscriptions existed. Fail open — see FindAsync.
        if (subscription is null)
        {
            return;
        }

        var now = clock.UtcNow;
        if (subscription.IsManagementUnlocked(now))
        {
            return;
        }

        var status = subscription.EffectiveStatus(now);

        throw status == SubscriptionStatus.TrialExpired
            ? new PaymentRequiredException(
                "Your order management trial has ended. Your storefront is still live.",
                PaymentRequiredReasons.TrialExpired)
            : new PaymentRequiredException(
                "Order management is not active on this store.",
                PaymentRequiredReasons.SubscriptionInactive);
    }

    /// <summary>
    /// Throws when publishing another product would exceed the free limit.
    ///
    /// Only ever called before *creating* a product. Deleting is always allowed, and existing
    /// products over the limit are never hidden or removed — a seller who drops to the free plan
    /// keeps everything they already had, they simply cannot add more.
    /// </summary>
    public async Task RequireProductHeadroomAsync(Guid storeId, CancellationToken cancellationToken)
    {
        if (plan.FreeProductLimit is not { } limit)
        {
            // Not configured, so not enforced. Better than inventing a number.
            return;
        }

        var subscription = await FindAsync(storeId, cancellationToken);

        // A paid subscription lifts the limit. A trial does not: the trial is for order management,
        // and the storefront tier is what the product count belongs to.
        if (subscription?.Status == SubscriptionStatus.Active)
        {
            return;
        }

        var published = await db.Products
            .CountAsync(p => p.StoreId == storeId && p.DeletedAt == null, cancellationToken);

        if (published < limit)
        {
            return;
        }

        throw new PaymentRequiredException(
            $"The free plan includes {limit} products. Remove one, or upgrade to add more.",
            PaymentRequiredReasons.FreeProductLimitReached);
    }
}
