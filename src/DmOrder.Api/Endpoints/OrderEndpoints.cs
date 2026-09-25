using System.Security.Cryptography;
using System.Text;
using DmOrder.Api.Common;
using DmOrder.Application.Features.Catalogue;
using DmOrder.Application.Features.CustomFields;
using DmOrder.Application.Features.Media;
using DmOrder.Application.Features.Orders;
using Microsoft.AspNetCore.RateLimiting;

namespace DmOrder.Api.Endpoints;

public static class CustomFieldEndpoints
{
    public static RouteGroupBuilder MapSellerCustomFieldEndpoints(this RouteGroupBuilder group)
    {
        var fields = group.MapGroup("/custom-fields").WithTags("Custom fields");

        fields.MapGet("/", async (
                Guid? productId,
                ListCustomFieldsHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(productId, cancellationToken)))
            .WithName("ListCustomFields")
            .WithSummary("The seller's questions. Filter by product, or omit for all.");

        fields.MapPost("/", async (
                CreateCustomFieldRequest request,
                CreateCustomFieldHandler handler,
                CancellationToken cancellationToken) =>
            {
                var created = await handler.HandleAsync(request, cancellationToken);
                return Results.Created($"/api/v1/seller/custom-fields/{created.Id}", created);
            })
            .WithValidation<CreateCustomFieldRequest>()
            .WithName("CreateCustomField");

        fields.MapPut("/{fieldId:guid}", async (
                Guid fieldId,
                UpdateCustomFieldRequest request,
                UpdateCustomFieldHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(fieldId, request, cancellationToken)))
            .WithValidation<UpdateCustomFieldRequest>()
            .WithName("UpdateCustomField");

        fields.MapDelete("/{fieldId:guid}", async (
                Guid fieldId,
                DeleteCustomFieldHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(fieldId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteCustomField")
            .WithSummary("Remove a question. Answers already given are kept and still render.");

        fields.MapPost("/reorder", async (
                ReorderRequest request,
                ReorderCustomFieldsHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(request.IdsInOrder, cancellationToken);
                return Results.NoContent();
            })
            .WithValidation<ReorderRequest>()
            .WithName("ReorderCustomFields");

        return group;
    }
}

public static class PublicOrderEndpoints
{
    public static RouteGroupBuilder MapPublicOrderEndpoints(this RouteGroupBuilder group)
    {
        var stores = group.MapGroup("/stores").WithTags("Storefront");

        stores.MapPost("/{slug}/orders", async (
                string slug,
                SubmitOrderRequest request,
                HttpContext httpContext,
                SubmitOrderHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    slug, request, HashClientIp(httpContext), cancellationToken);

                return Results.Ok(result);
            })
            .WithValidation<SubmitOrderRequest>()
            .RequireRateLimiting(RateLimitPolicies.PublicWrite)
            .WithName("SubmitOrder")
            .WithSummary("Submit an order. Anonymous, rate limited, and validated against the seller's questions.");

        stores.MapPost("/{slug}/order-images", async (
                string slug,
                HttpRequest request,
                UploadOrderReferenceHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest(new { error = "Send the photo as multipart/form-data." });
                }

                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files["file"];

                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(new { error = "Choose a photo to upload." });
                }

                await using var stream = file.OpenReadStream();

                var result = await handler.HandleAsync(slug, stream, file.Length, cancellationToken);
                return Results.Ok(result);
            })
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicies.PublicWrite)
            .WithName("UploadOrderReferenceImage")
            .WithSummary("A customer's reference photo. Anonymous, so size- and byte-checked.");

        // POST rather than GET, and the phone is in the body rather than the query string. A phone
        // number in a URL ends up in access logs, browser history and any referrer header the page
        // later sends - none of which is an acceptable place for a customer's personal data.
        //
        // Rate limited like the other anonymous write paths: the reference is short enough that an
        // unthrottled endpoint would be worth guessing against.
        group.MapPost("/orders/track", async (
                TrackOrderRequest request,
                TrackOrderHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(request, cancellationToken)))
            .WithValidation<TrackOrderRequest>()
            .RequireRateLimiting(RateLimitPolicies.PublicWrite)
            .WithName("TrackOrder")
            .WithSummary(
                "Look up an order by its public reference and the phone it was placed with. "
                + "No account needed. Every failure answers 404 so it cannot be used to probe references.");

        return group;
    }

    /// <summary>
    /// Hashes the caller's address before it is stored.
    ///
    /// Enough to spot one address flooding a store, without keeping an identifier for someone who
    /// never agreed to be tracked. Salted with the store slug so the same visitor across two stores
    /// does not produce a matching hash — that would let the platform correlate them.
    /// </summary>
    private static string? HashClientIp(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var salt = httpContext.Request.RouteValues["slug"]?.ToString() ?? string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}|{address}"));

        return Convert.ToHexString(bytes);
    }
}
