using DmOrder.Application.Common.Interfaces;
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

        return new AdminStatsResponse(
            totalSellers,
            activeSellers,
            totalStores,
            liveStores,
            suspendedStores,
            totalProducts,
            totalOrders,
            recentOrders);
    }
}
