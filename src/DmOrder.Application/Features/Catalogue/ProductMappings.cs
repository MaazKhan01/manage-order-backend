using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Catalogue;

internal static class ProductMappings
{
    /// <summary>
    /// Builds the detail response, resolving every image URL in one query rather than one per image.
    /// </summary>
    public static async Task<ProductDetailResponse> ToDetailAsync(
        IAppDbContext db,
        IFileStorage storage,
        Product product,
        CancellationToken cancellationToken)
    {
        var mediaIds = product.Images.Select(i => i.MediaId).ToArray();

        var keys = mediaIds.Length == 0
            ? []
            : await db.MediaAssets
                .Where(m => mediaIds.Contains(m.Id) && m.StoreId == product.StoreId)
                .Select(m => new { m.Id, m.StorageKey })
                .ToDictionaryAsync(m => m.Id, m => m.StorageKey, cancellationToken);

        var categoryName = product.CategoryId is { } categoryId
            ? await db.Categories
                .Where(c => c.Id == categoryId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var images = product.Images
            .OrderBy(i => i.DisplayOrder)
            .Where(i => keys.ContainsKey(i.MediaId))
            .Select(i => new ProductImageResponse(
                i.Id,
                storage.GetPublicUrl(keys[i.MediaId]),
                i.AltText,
                i.DisplayOrder))
            .ToList();

        return new ProductDetailResponse(
            product.Id,
            product.Name,
            product.Slug,
            product.Description,
            product.Price,
            product.PriceIsFrom,
            product.IsActive,
            product.AcceptsCustomOrder,
            product.DisplayOrder,
            product.CategoryId,
            categoryName,
            images);
    }
}
