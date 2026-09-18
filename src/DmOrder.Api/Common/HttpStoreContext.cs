using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Api.Common;

/// <summary>
/// Resolves the seller's store from the <c>store_id</c> claim issued at login.
///
/// The claim is the *only* source. A store id arriving in a route, query string, header or body is
/// attacker-controlled and is never trusted here — that is what makes IDOR attempts fail.
/// </summary>
public sealed class HttpStoreContext(IHttpContextAccessor httpContextAccessor) : IStoreContext
{
    public const string StoreIdClaimType = "store_id";

    public Guid? StoreId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst(StoreIdClaimType)?.Value;
            return Guid.TryParse(claim, out var storeId) ? storeId : null;
        }
    }

    public Guid RequireStoreId() =>
        StoreId ?? throw new ForbiddenException("The current user does not have a store.");
}
