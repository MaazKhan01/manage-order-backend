using DmOrder.Application.Common.Interfaces;

namespace DmOrder.Application.Features.Catalogue;

public sealed class AddProductImageHandler(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    public async Task<ProductDetailResponse> HandleAsync(
        Guid productId,
        AddProductImageRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var product = await CatalogueGuards.RequireOwnProductAsync(db, storeId, productId, cancellationToken);

        // The media id comes from the client. Without this check a seller could attach another
        // seller's upload and serve it from their own storefront.
        await CatalogueGuards.RequireMediaBelongsToStoreAsync(db, storeId, request.MediaId, cancellationToken);

        product.AddImage(request.MediaId, request.AltText);
        await db.SaveChangesAsync(cancellationToken);

        return await ProductMappings.ToDetailAsync(db, storage, product, cancellationToken);
    }
}

public sealed class RemoveProductImageHandler(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    public async Task<ProductDetailResponse> HandleAsync(
        Guid productId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var product = await CatalogueGuards.RequireOwnProductAsync(db, storeId, productId, cancellationToken);

        // The MediaAsset row and the stored file are deliberately left alone: the seller may still be
        // using the image elsewhere, and orphan cleanup is a background concern, not a request-path one.
        product.RemoveImage(imageId);
        await db.SaveChangesAsync(cancellationToken);

        return await ProductMappings.ToDetailAsync(db, storage, product, cancellationToken);
    }
}

public sealed class ReorderProductImagesHandler(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    public async Task<ProductDetailResponse> HandleAsync(
        Guid productId,
        ReorderRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var product = await CatalogueGuards.RequireOwnProductAsync(db, storeId, productId, cancellationToken);

        // The first image is the one the storefront shows in a listing, so ordering is a real editorial
        // decision rather than a cosmetic one.
        product.ReorderImages(request.IdsInOrder);
        await db.SaveChangesAsync(cancellationToken);

        return await ProductMappings.ToDetailAsync(db, storage, product, cancellationToken);
    }
}
