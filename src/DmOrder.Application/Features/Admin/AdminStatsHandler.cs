using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Admin;

/// <summary>
/// Platform-wide counts.
///
/// This is the one place in the product that deliberately reads across every tenant. Everything else
/// is scoped to the caller's store; this is scoped by the Admin policy on the route group instead.
/// </summary>
public sealed class AdminStatsHandler(IAppDbContext db, IUserDirectory users, IDateTimeProvider clock)
{
    public async Task<AdminStatsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var (totalSellers, activeSellers) = await users.CountSellersAsync(cancellationToken);

        var thirtyDaysAgo = clock.UtcNow.AddDays(-30);

        var totalStores = await db.Stores.CountAsync(cancellationToken);
        var liveStores = await db.Stores.CountAsync(s => s.IsPublished && s.IsActive, cancellationToken);
        var suspendedStores = await db.Stores.CountAsync(s => !s.IsActive, cancellationToken);
        var totalProducts = await db.Products.CountAsync(cancellationToken);
        var totalOrders = await db.Orders.CountAsync(cancellationToken);
        var recentOrders = await db.Orders.CountAsync(o => o.CreatedAt >= thirtyDaysAgo, cancellationToken);

        var now = clock.UtcNow;
        var weekAgo = now.AddDays(-7);
        var twoWeeksAgo = now.AddDays(-14);
        var weekAhead = now.AddDays(7);

        var (newThisWeek, newPreviousWeek) = await users.CountSignupsAsync(
            weekAgo, twoWeeksAgo, cancellationToken);

        /*
         * Subscription state is computed against the clock, not read from the column.
         *
         * A row still marked Trialing whose TrialEndsAt has passed is an expired trial - nothing
         * rewrites that column when the date goes by. Trusting it would overstate the one number
         * that actually says whether this business works.
         */
        var trialing = await db.Subscriptions.CountAsync(
            x => x.Status == SubscriptionStatus.Trialing && x.TrialEndsAt > now, cancellationToken);

        var expiredTrials = await db.Subscriptions.CountAsync(
            x => x.Status == SubscriptionStatus.TrialExpired
                 || (x.Status == SubscriptionStatus.Trialing && x.TrialEndsAt <= now),
            cancellationToken);

        var paid = await db.Subscriptions.CountAsync(
            x => x.Status == SubscriptionStatus.Active, cancellationToken);

        var endingSoon = await db.Subscriptions.CountAsync(
            x => x.Status == SubscriptionStatus.Trialing
                 && x.TrialEndsAt > now
                 && x.TrialEndsAt <= weekAhead,
            cancellationToken);

        // Sellers who stopped partway. Both are read as "no rows exist" rather than a join count,
        // so a store with soft-deleted products still counts as empty - which it is, to a visitor.
        var storesWithNoProducts = await db.Stores.CountAsync(
            store => !db.Products.Any(p => p.StoreId == store.Id && p.DeletedAt == null),
            cancellationToken);

        var liveStoresWithNoOrders = await db.Stores.CountAsync(
            store => store.IsPublished
                     && store.IsActive
                     && !db.Orders.Any(o => o.StoreId == store.Id),
            cancellationToken);

        return new AdminStatsResponse(
            totalSellers,
            activeSellers,
            totalStores,
            liveStores,
            suspendedStores,
            totalProducts,
            totalOrders,
            recentOrders,
            newThisWeek,
            newPreviousWeek,
            trialing,
            paid,
            expiredTrials,
            endingSoon,
            storesWithNoProducts,
            liveStoresWithNoOrders);
    }
}
