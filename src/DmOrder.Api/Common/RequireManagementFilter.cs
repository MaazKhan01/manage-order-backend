using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.Billing;

namespace DmOrder.Api.Common;

/// <summary>
/// Blocks order-management routes when the store's subscription does not cover them.
///
/// Applied to whole route groups rather than to individual endpoints on purpose: a paywall that has
/// to be remembered per handler is a paywall with a hole in it. Adding an order endpoint to the
/// group inherits the check; there is nothing to forget.
///
/// The store id comes from <see cref="IStoreContext"/>, which reads the token. Nothing the client
/// sends is involved.
/// </summary>
public sealed class RequireManagementFilter(
    IStoreContext storeContext,
    SubscriptionGuard guard) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        // No store yet means there is nothing to manage; the handler's own not-found is the right
        // answer, not a billing message.
        if (storeContext.StoreId is { } storeId)
        {
            // Throws PaymentRequiredException, which GlobalExceptionHandler turns into a 402 carrying
            // the machine-readable reason.
            await guard.RequireManagementAsync(storeId, context.HttpContext.RequestAborted);
        }

        return await next(context);
    }
}

public static class ManagementFilterExtensions
{
    /// <summary>
    /// Marks a group as part of paid order management.
    ///
    /// Deliberately explicit at the call site in <c>ApiEndpoints</c>, so which half of the product a
    /// route belongs to is visible in one file rather than distributed across handlers.
    /// </summary>
    public static RouteGroupBuilder RequiresOrderManagement(this RouteGroupBuilder group) =>
        group.AddEndpointFilter<RequireManagementFilter>();
}
