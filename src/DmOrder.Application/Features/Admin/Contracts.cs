namespace DmOrder.Application.Features.Admin;

/// <summary>
/// What the platform owner sees on opening the admin area.
///
/// Deliberately counts, not analytics. Anything trend-shaped is out of scope for V1, and a number a
/// person can act on beats a chart nobody reads.
/// </summary>
public sealed record AdminStatsResponse(
    int TotalSellers,
    int ActiveSellers,
    int TotalStores,
    // Published by the seller *and* not suspended by the admin — what a visitor can actually reach.
    int LiveStores,
    int SuspendedStores,
    int TotalProducts,
    int TotalOrders,
    int OrdersLast30Days);

/// <summary>A store as the admin lists it. Carries its owner, because that is who gets contacted.</summary>
public sealed record AdminStoreListItemResponse(
    Guid Id,
    string Name,
    string Slug,
    string? City,
    string? Country,
    string Currency,
    bool IsPublished,
    bool IsActive,
    string OwnerName,
    string OwnerEmail,
    bool OwnerIsActive,
    int ProductCount,
    int OrderCount,
    DateTimeOffset CreatedAt);

/// <summary>A seller account as the admin lists it, with the store they own if they have set one up.</summary>
public sealed record AdminSellerListItemResponse(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsActive,
    string? StoreName,
    string? StoreSlug,
    bool? StoreIsPublished,
    int OrderCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// The admin's switch, for a store or a seller account.
///
/// Suspending never deletes: a suspended store's orders and customers are untouched, and restoring it
/// puts it straight back. The platform has no destructive admin action in V1 on purpose.
/// </summary>
public sealed record SetActiveRequest(bool IsActive);
