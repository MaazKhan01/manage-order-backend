using DmOrder.Application.Features.Billing;

namespace DmOrder.Api.Endpoints;

/// <summary>
/// Where a seller stands commercially.
///
/// Free, and deliberately *not* behind the order-management paywall: someone locked out has to be
/// able to read why. Putting this behind the same filter would leave them staring at a 402 with no
/// explanation.
/// </summary>
public static class SubscriptionEndpoints
{
    public static RouteGroupBuilder MapSellerSubscriptionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/subscription", async (
                GetSubscriptionHandler handler,
                CancellationToken cancellationToken) =>
            {
                var subscription = await handler.HandleAsync(cancellationToken);

                // 204 rather than 404: a seller with no store yet is a normal state on the way to
                // having one, not a missing resource.
                return subscription is null ? Results.NoContent() : Results.Ok(subscription);
            })
            .WithName("GetSubscription")
            .WithSummary(
                "The store's plan, trial state and product allowance. Carries no prices - pricing is not set.");

        return group;
    }
}
