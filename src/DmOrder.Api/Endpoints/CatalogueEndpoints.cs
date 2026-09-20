using DmOrder.Api.Common;
using DmOrder.Application.Features.Catalogue;

namespace DmOrder.Api.Endpoints;

public static class CatalogueEndpoints
{
    public static RouteGroupBuilder MapSellerCatalogueEndpoints(this RouteGroupBuilder group)
    {
        MapCategories(group);
        MapProducts(group);
        return group;
    }

    private static void MapCategories(RouteGroupBuilder group)
    {
        var categories = group.MapGroup("/categories").WithTags("Categories");

        categories.MapGet("/", async (ListCategoriesHandler handler, CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("ListCategories");

        categories.MapPost("/", async (
                CreateCategoryRequest request,
                CreateCategoryHandler handler,
                CancellationToken cancellationToken) =>
            {
                var created = await handler.HandleAsync(request, cancellationToken);
                return Results.Created($"/api/v1/seller/categories/{created.Id}", created);
            })
            .WithValidation<CreateCategoryRequest>()
            .WithName("CreateCategory")
            .WithSummary("Create a category. The web address is derived from the name if omitted.");

        categories.MapPut("/{categoryId:guid}", async (
                Guid categoryId,
                UpdateCategoryRequest request,
                UpdateCategoryHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(categoryId, request, cancellationToken)))
            .WithValidation<UpdateCategoryRequest>()
            .WithName("UpdateCategory");

        categories.MapDelete("/{categoryId:guid}", async (
                Guid categoryId,
                DeleteCategoryHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(categoryId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteCategory")
            .WithSummary("Delete a category. Its products are kept and become uncategorised.");

        categories.MapPost("/reorder", async (
                ReorderRequest request,
                ReorderCategoriesHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(request, cancellationToken);
                return Results.NoContent();
            })
            .WithValidation<ReorderRequest>()
            .WithName("ReorderCategories");
    }

    private static void MapProducts(RouteGroupBuilder group)
    {
        var products = group.MapGroup("/products").WithTags("Products");

        products.MapGet("/", async (
                int? page,
                int? pageSize,
                Guid? categoryId,
                string? search,
                bool? isActive,
                string? sort,
                ListProductsHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(
                    new ProductListQuery(page, pageSize, categoryId, search, isActive, sort),
                    cancellationToken)))
            .WithName("ListProducts")
            .WithSummary("The seller's products, paginated and filterable.");

        products.MapGet("/{productId:guid}", async (
                Guid productId,
                GetProductHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(productId, cancellationToken)))
            .WithName("GetProduct");

        products.MapPost("/", async (
                CreateProductRequest request,
                CreateProductHandler handler,
                CancellationToken cancellationToken) =>
            {
                var created = await handler.HandleAsync(request, cancellationToken);
                return Results.Created($"/api/v1/seller/products/{created.Id}", created);
            })
            .WithValidation<CreateProductRequest>()
            .WithName("CreateProduct");

        products.MapPut("/{productId:guid}", async (
                Guid productId,
                UpdateProductRequest request,
                UpdateProductHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(productId, request, cancellationToken)))
            .WithValidation<UpdateProductRequest>()
            .WithName("UpdateProduct");

        products.MapDelete("/{productId:guid}", async (
                Guid productId,
                DeleteProductHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(productId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteProduct")
            .WithSummary("Remove a product. Kept internally so existing orders still render.");

        products.MapPost("/reorder", async (
                ReorderRequest request,
                ReorderProductsHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(request, cancellationToken);
                return Results.NoContent();
            })
            .WithValidation<ReorderRequest>()
            .WithName("ReorderProducts");

        products.MapPost("/{productId:guid}/images", async (
                Guid productId,
                AddProductImageRequest request,
                AddProductImageHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(productId, request, cancellationToken)))
            .WithValidation<AddProductImageRequest>()
            .WithName("AddProductImage")
            .WithSummary("Attach an already-uploaded image to a product.");

        products.MapDelete("/{productId:guid}/images/{imageId:guid}", async (
                Guid productId,
                Guid imageId,
                RemoveProductImageHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(productId, imageId, cancellationToken)))
            .WithName("RemoveProductImage");

        products.MapPost("/{productId:guid}/images/reorder", async (
                Guid productId,
                ReorderRequest request,
                ReorderProductImagesHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(productId, request, cancellationToken)))
            .WithValidation<ReorderRequest>()
            .WithName("ReorderProductImages")
            .WithSummary("Reorder images. The first is the one shown in listings.");
    }

    public static RouteGroupBuilder MapPublicCatalogueEndpoints(this RouteGroupBuilder group)
    {
        var stores = group.MapGroup("/stores").WithTags("Storefront");

        stores.MapGet("/{slug}/products", async (
                string slug,
                GetPublicCatalogueHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(slug, cancellationToken)))
            .WithName("GetPublicCatalogue")
            .WithSummary("Active products of a published store, grouped by category.");

        stores.MapGet("/{slug}/products/{productSlug}", async (
                string slug,
                string productSlug,
                GetPublicProductHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(slug, productSlug, cancellationToken)))
            .WithName("GetPublicProduct");

        return group;
    }
}
