using DmOrder.Api.Common;
using DmOrder.Application.Features.Admin;

namespace DmOrder.Api.Endpoints;

/// <summary>
/// The platform owner's view.
///
/// Every route here reads across tenants, which is exactly what the rest of the API is built to
/// prevent. The only thing making that legitimate is the Admin policy on the parent group in
/// <see cref="ApiEndpoints"/> — so nothing in this file may be moved to another group.
///
/// There is no destructive action. Suspending a store or an account is reversible and keeps the
/// data; deleting a seller's orders is not something a support conversation should be able to do.
/// </summary>
public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/stats", async (AdminStatsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("GetAdminStats")
            .WithSummary("Platform-wide counts for the admin overview.");

        var stores = group.MapGroup("/stores");

        stores.MapGet("/", async (
                int? page,
                int? pageSize,
                string? search,
                AdminStoreFilter? filter,
                string? sort,
                ListAdminStoresHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(
                    new AdminStoreListQuery(page, pageSize, search, filter, sort), cancellationToken)))
            .WithName("ListAdminStores")
            .WithSummary("Every store on the platform, newest first, with its owner.");

        stores.MapPut("/{storeId:guid}/status", async (
                Guid storeId,
                SetActiveRequest request,
                SetStoreActiveHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(storeId, request, cancellationToken);
                return Results.NoContent();
            })
            .WithName("SetStoreActive")
            .WithSummary("Suspend or restore a storefront. Reversible; nothing is deleted.");

        var sellers = group.MapGroup("/sellers");

        sellers.MapGet("/", async (
                int? page,
                int? pageSize,
                string? search,
                bool? isActive,
                string? sort,
                ListAdminSellersHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(
                    new AdminSellerListQuery(page, pageSize, search, isActive, sort), cancellationToken)))
            .WithName("ListAdminSellers")
            .WithSummary("Seller accounts, newest first, with the store each one owns.");

        sellers.MapPut("/{userId:guid}/status", async (
                Guid userId,
                SetActiveRequest request,
                SetSellerActiveHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(userId, request, cancellationToken);
                return Results.NoContent();
            })
            .WithName("SetSellerActive")
            .WithSummary("Suspend or restore a seller account. Only accounts in the Seller role.");

        return group;
    }
}
