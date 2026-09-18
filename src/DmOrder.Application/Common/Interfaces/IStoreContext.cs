namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// The store (tenant) the current *authenticated seller* is acting as.
///
/// This is derived from the authenticated identity only — never from a route value, query string,
/// header or request body. A client-supplied store id is always untrusted input.
///
/// Public storefront requests deliberately do NOT populate this. They resolve a published store by
/// slug inside their own handlers, so the anonymous read path and the seller write path can never be
/// confused for one another.
/// </summary>
public interface IStoreContext
{
    /// <summary>The seller's store id, or null when the request is anonymous or the seller has no store yet.</summary>
    Guid? StoreId { get; }

    /// <summary>Returns the seller's store id, or throws if the caller has no store.</summary>
    Guid RequireStoreId();
}
