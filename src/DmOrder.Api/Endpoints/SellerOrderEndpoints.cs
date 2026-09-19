using DmOrder.Api.Common;
using DmOrder.Application.Features.Orders;
using DmOrder.Domain.Orders;

namespace DmOrder.Api.Endpoints;

public static class SellerOrderEndpoints
{
    public static RouteGroupBuilder MapSellerOrderEndpoints(this RouteGroupBuilder group)
    {
        var orders = group.MapGroup("/orders").WithTags("Orders");

        orders.MapGet("/", async (
                int? page,
                int? pageSize,
                OrderStatus? status,
                string? search,
                ListOrdersHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(
                    new OrderListQuery(page, pageSize, status, search), cancellationToken)))
            .WithName("ListOrders")
            .WithSummary("The seller's orders, newest first. Filter by status, search by customer or number.");

        orders.MapGet("/counts", async (GetOrderCountsHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("GetOrderCounts")
            .WithSummary("Order counts per status, for the list's filter tabs.");

        orders.MapGet("/{orderId:guid}", async (
                Guid orderId,
                GetOrderHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(orderId, cancellationToken)))
            .WithName("GetOrder")
            .WithSummary("One order with its answers, history and internal notes.");

        orders.MapPut("/{orderId:guid}/status", async (
                Guid orderId,
                ChangeOrderStatusRequest request,
                ChangeOrderStatusHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(orderId, request, cancellationToken);
                return Results.NoContent();
            })
            .WithValidation<ChangeOrderStatusRequest>()
            .WithName("ChangeOrderStatus")
            .WithSummary("Move an order along. Recorded in its history.");

        orders.MapPut("/{orderId:guid}/payment", async (
                Guid orderId,
                ChangeOrderPaymentRequest request,
                ChangeOrderPaymentHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(orderId, request, cancellationToken);
                return Results.NoContent();
            })
            .WithValidation<ChangeOrderPaymentRequest>()
            .WithName("ChangeOrderPayment")
            .WithSummary("Record payment state and the agreed total.");

        orders.MapPost("/{orderId:guid}/notes", async (
                Guid orderId,
                AddOrderNoteRequest request,
                AddOrderNoteHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(orderId, request, cancellationToken)))
            .WithValidation<AddOrderNoteRequest>()
            .WithName("AddOrderNote")
            .WithSummary("A private note. Never returned by any public endpoint.");

        orders.MapDelete("/{orderId:guid}/notes/{noteId:guid}", async (
                Guid orderId,
                Guid noteId,
                DeleteOrderNoteHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(orderId, noteId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteOrderNote");

        return group;
    }

    public static RouteGroupBuilder MapSellerCustomerEndpoints(this RouteGroupBuilder group)
    {
        var customers = group.MapGroup("/customers").WithTags("Customers");

        customers.MapGet("/", async (
                int? page,
                int? pageSize,
                string? search,
                ListCustomersHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(
                    new CustomerListQuery(page, pageSize, search), cancellationToken)))
            .WithName("ListCustomers")
            .WithSummary("Everyone who has ordered, built from their orders. Customers never register.");

        customers.MapGet("/{customerId:guid}", async (
                Guid customerId,
                GetCustomerHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(customerId, cancellationToken)))
            .WithName("GetCustomer")
            .WithSummary("One customer and their order history with this store.");

        return group;
    }
}
