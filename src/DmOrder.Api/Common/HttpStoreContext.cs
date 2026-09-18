using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Identity;

namespace DmOrder.Api.Common;

/// <summary>
/// Resolves the seller's store from the <c>store_id</c> claim issued at login and refresh.
///
/// The claim is the *only* source. A store id arriving in a route, query string, header or body is
/// attacker-controlled and is never trusted here — that is what makes IDOR attempts fail.
///
/// The claim is populated from the database when tokens are issued, so it becomes available once the
/// seller creates their store (Phase 3) and their tokens are reissued.
/// </summary>
public sealed class HttpStoreContext(IHttpContextAccessor httpContextAccessor) : IStoreContext
{
    public Guid? StoreId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst(AppClaimTypes.StoreId)?.Value;
            return Guid.TryParse(claim, out var storeId) ? storeId : null;
        }
    }

    public Guid RequireStoreId() =>
        StoreId ?? throw new ForbiddenException("The current user does not have a store.");
}
