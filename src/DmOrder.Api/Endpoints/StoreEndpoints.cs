using DmOrder.Api.Common;
using DmOrder.Application.Features.Media;
using DmOrder.Application.Features.Stores;
using DmOrder.Domain.Media;

namespace DmOrder.Api.Endpoints;

public static class StoreEndpoints
{
    /// <summary>
    /// The seller's own store. No endpoint here takes a store id — the store is resolved from the
    /// authenticated identity, so there is nothing for a caller to tamper with.
    /// </summary>
    public static RouteGroupBuilder MapSellerStoreEndpoints(this RouteGroupBuilder group)
    {
        var store = group.MapGroup("/store").WithTags("Store");

        store.MapGet("/", async (GetMyStoreHandler handler, CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(cancellationToken);

                // 204 rather than 404: "you have no store yet" is a normal state for a new seller, not
                // an error, and the frontend routes on it.
                return result is null ? Results.NoContent() : Results.Ok(result);
            })
            .WithName("GetMyStore")
            .WithSummary("The signed-in seller's store, or 204 if they have not created one.");

        store.MapPost("/", async (
                CreateStoreRequest request,
                CreateStoreHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(request, cancellationToken);
                return Results.Created($"/api/v1/public/stores/{result.Slug}", result);
            })
            .WithValidation<CreateStoreRequest>()
            .WithName("CreateStore")
            .WithSummary("Create the seller's store.");

        store.MapPut("/", async (
                UpdateStoreProfileRequest request,
                UpdateStoreProfileHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(request, cancellationToken)))
            .WithValidation<UpdateStoreProfileRequest>()
            .WithName("UpdateStoreProfile")
            .WithSummary("Update store profile, contact details and SEO.");

        store.MapPut("/slug", async (
                ChangeStoreSlugRequest request,
                ChangeStoreSlugHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(request, cancellationToken)))
            .WithValidation<ChangeStoreSlugRequest>()
            .WithName("ChangeStoreSlug")
            .WithSummary("Change the public address. Breaks links the seller has already shared.");

        store.MapPut("/theme", async (
                UpdateStoreThemeRequest request,
                UpdateStoreThemeHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(request, cancellationToken)))
            .WithValidation<UpdateStoreThemeRequest>()
            .WithName("UpdateStoreTheme")
            .WithSummary("Update colours, typography, button style and layout.");

        store.MapPost("/publish", async (PublishStoreHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(publish: true, cancellationToken)))
            .WithName("PublishStore")
            .WithSummary("Make the storefront public.");

        store.MapPost("/unpublish", async (PublishStoreHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(publish: false, cancellationToken)))
            .WithName("UnpublishStore")
            .WithSummary("Take the storefront offline.");

        store.MapPut("/logo", async (
                SetStoreImageRequest request,
                SetStoreImageHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(MediaPurpose.StoreLogo, request, cancellationToken)))
            .WithName("SetStoreLogo");

        store.MapPut("/cover", async (
                SetStoreImageRequest request,
                SetStoreImageHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(MediaPurpose.StoreCover, request, cancellationToken)))
            .WithName("SetStoreCover");

        store.MapPut("/background", async (
                SetStoreImageRequest request,
                SetStoreImageHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(MediaPurpose.StoreBackground, request, cancellationToken)))
            .WithName("SetStoreBackground");

        return group;
    }

    public static RouteGroupBuilder MapPublicStoreEndpoints(this RouteGroupBuilder group)
    {
        var stores = group.MapGroup("/stores").WithTags("Storefront");

        stores.MapGet("/{slug}", async (
                string slug,
                GetPublicStoreHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(slug, cancellationToken)))
            .WithName("GetPublicStore")
            .WithSummary("A published storefront. 404 when unpublished, suspended or non-existent.");

        stores.MapGet("/slug-available", async (
                string? slug,
                CheckSlugAvailabilityHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(slug, cancellationToken)))
            .WithName("CheckSlugAvailability")
            .WithSummary("Whether a store address can be used.");

        return group;
    }
}

public static class MediaEndpoints
{
    public static RouteGroupBuilder MapSellerMediaEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/media", async (
                HttpRequest request,
                UploadMediaHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest(new { error = "Send the image as multipart/form-data." });
                }

                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files["file"];

                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(new { error = "Choose an image to upload." });
                }

                if (!Enum.TryParse<MediaPurpose>(form["purpose"], ignoreCase: true, out var purpose))
                {
                    return Results.BadRequest(new { error = "Specify what the image is for." });
                }

                await using var stream = file.OpenReadStream();

                var result = await handler.HandleAsync(
                    stream, file.FileName, file.Length, purpose, cancellationToken);

                return Results.Ok(result);
            })
            .DisableAntiforgery()
            .WithName("UploadMedia")
            .WithSummary("Upload an image. Validated by its actual bytes, not its declared type.")
            .WithTags("Media");

        return group;
    }
}
