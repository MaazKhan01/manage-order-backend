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
    int OrdersLast30Days,

    // --- Is the platform growing? ---
    // Two adjacent weeks rather than a trend line: the only question a number can honestly answer
    // at this size is "more or fewer than last week", and that needs exactly two numbers.
    int NewSellersThisWeek,
    int NewSellersPreviousWeek,

    // --- Where the money is, or is not ---
    // Computed against the clock, not read from the status column: a row still marked Trialing whose
    // trial end has passed is an expired trial, and counting it as active would flatter the number
    // that matters most.
    int Trialing,
    int PaidSubscriptions,
    int ExpiredTrials,
    /// <summary>Trials ending within a week. The only genuinely time-sensitive number here.</summary>
    int TrialsEndingSoon,

    // --- Sellers who got stuck ---
    /// <summary>A store with no products is someone who signed up and stopped.</summary>
    int StoresWithNoProducts,
    /// <summary>Live, reachable, and nobody has ever ordered. The shop works; the traffic does not.</summary>
    int LiveStoresWithNoOrders);

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
