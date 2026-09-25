using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Billing;

/// <summary>
/// What the dashboard needs to tell a seller where they stand.
///
/// **No amounts.** Pricing is not set, and this contract carries none — the UI renders plan names
/// and states. When pricing exists it arrives here as its own fields rather than by reinterpreting
/// these.
/// </summary>
public sealed record SubscriptionResponse(
    string Status,
    bool IsManagementUnlocked,
    DateTimeOffset TrialStartedAt,
    DateTimeOffset TrialEndsAt,
    int? TrialDaysRemaining,
    DateTimeOffset? CurrentPeriodEnd,
    /// <summary>The plan name to show. Never a price.</summary>
    string PlanName,
    int ProductCount,
    /// <summary>Null when no free limit is configured, in which case none is enforced.</summary>
    int? FreeProductLimit,
    /// <summary>
    /// Whether a seller can actually start a subscription right now. False until a payment provider
    /// is configured — the UI must not offer an upgrade button that cannot do anything.
    /// </summary>
    bool CanUpgrade,
    /// <summary>Why not, when they cannot. Shown honestly rather than hidden.</summary>
    string? UpgradeUnavailableReason);

public sealed class GetSubscriptionHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IPlanPolicy plan,
    IPaymentProvider payments)
{
    public async Task<SubscriptionResponse?> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var storeId = await db.Stores
            .Where(s => s.OwnerUserId == userId)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // No store yet means no subscription yet. The caller renders store setup, not billing.
        if (storeId is not { } id)
        {
            return null;
        }

        var subscription = await db.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == id, cancellationToken);

        var productCount = await db.Products
            .CountAsync(p => p.StoreId == id && p.DeletedAt == null, cancellationToken);

        var now = clock.UtcNow;

        // A store predating billing has no row. It is treated as unlocked, consistent with
        // SubscriptionGuard, and reported as such rather than invented as a trial.
        if (subscription is null)
        {
            return new SubscriptionResponse(
                SubscriptionStatus.Active.ToString(),
                IsManagementUnlocked: true,
                TrialStartedAt: now,
                TrialEndsAt: now,
                TrialDaysRemaining: null,
                CurrentPeriodEnd: null,
                PlanName: PlanNames.OrderManagement,
                productCount,
                plan.FreeProductLimit,
                payments.IsConfigured,
                payments.IsConfigured ? null : NotConfigured);
        }

        var status = subscription.EffectiveStatus(now);

        return new SubscriptionResponse(
            status.ToString(),
            subscription.IsManagementUnlocked(now),
            subscription.TrialStartedAt,
            subscription.TrialEndsAt,
            subscription.TrialDaysRemaining(now),
            subscription.CurrentPeriodEnd,
            PlanNames.For(status),
            productCount,
            plan.FreeProductLimit,
            payments.IsConfigured,
            payments.IsConfigured ? null : NotConfigured);
    }

    /// <summary>
    /// Said plainly. Pretending an upgrade is available and failing at the last step would be worse
    /// than saying so up front.
    /// </summary>
    private const string NotConfigured =
        "Subscriptions are not open yet. Your storefront stays free either way, and we will be in "
        + "touch before anything changes.";
}

/// <summary>
/// The plan names shown in the UI. Names only — pricing has not been set, and this file must not
/// acquire amounts by accident.
/// </summary>
public static class PlanNames
{
    public const string FreeStorefront = "Free Storefront";
    public const string OrderManagementTrial = "Order Management Trial";
    public const string OrderManagement = "Order Management";

    public static string For(SubscriptionStatus status) =>
        status switch
        {
            SubscriptionStatus.Trialing => OrderManagementTrial,
            SubscriptionStatus.Active => OrderManagement,
            // Everything else has fallen back to the free half of the product. Their storefront is
            // still live; that is the plan they are on.
            _ => FreeStorefront,
        };
}
