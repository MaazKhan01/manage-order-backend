using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.CustomFields;
using DmOrder.Domain.Catalogue;
using DmOrder.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Catalogue;

/// <summary>
/// The anonymous storefront catalogue.
///
/// Like the public store read, this never touches <see cref="IStoreContext"/>: it resolves a store
/// from a published slug, so the anonymous path and the seller path share no scoping logic.
/// </summary>
public sealed class GetPublicCatalogueHandler(IAppDbContext db, IFileStorage storage)
{
    public async Task<PublicCatalogueResponse> HandleAsync(string storeSlug, CancellationToken cancellationToken)
    {
        var storeId = await RequireVisibleStoreIdAsync(db, storeSlug, cancellationToken);

        // Inactive and deleted rows are filtered here, not in the renderer. A draft product must not
        // reach the client at all — hiding it in the UI would still ship it in the payload.
        var rows = await db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == storeId && p.DeletedAt == null && p.IsActive)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .Select(p => new CatalogueRow(
                p.Slug,
                p.Name,
                p.Description,
                p.Price,
                p.PriceIsFrom,
                p.AcceptsCustomOrder,
                p.CategoryId,
                db.ProductImages
                    .Where(i => i.ProductId == p.Id)
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => db.MediaAssets.Where(m => m.Id == i.MediaId).Select(m => m.StorageKey).FirstOrDefault())
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.StoreId == storeId && c.DeletedAt == null && c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Slug, c.Name })
            .ToListAsync(cancellationToken);

        PublicProductResponse ToResponse(CatalogueRow row) => new(
            row.Slug,
            row.Name,
            row.Description,
            row.Price,
            row.PriceIsFrom,
            row.AcceptsCustomOrder,
            row.PrimaryImageKey is null ? null : storage.GetPublicUrl(row.PrimaryImageKey));

        var grouped = categories
            .Select(c => new PublicCategoryResponse(
                c.Slug,
                c.Name,
                rows.Where(r => r.CategoryId == c.Id).Select(ToResponse).ToList()))
            // An empty category is noise on a storefront.
            .Where(c => c.Products.Count > 0)
            .ToList();

        var uncategorised = rows
            .Where(r => r.CategoryId is null)
            .Select(ToResponse)
            .ToList();

        return new PublicCatalogueResponse(grouped, uncategorised, rows.Count);
    }

    internal static async Task<Guid> RequireVisibleStoreIdAsync(
        IAppDbContext db,
        string storeSlug,
        CancellationToken cancellationToken)
    {
        var slug = storeSlug.Trim().ToLowerInvariant();

        var storeId = await db.Stores
            .Where(s => s.Slug == slug && s.IsPublished && s.IsActive)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Unpublished, suspended and non-existent are all the same answer.
        return storeId ?? throw new NotFoundException("Store", slug);
    }
}

public sealed class GetPublicProductHandler(IAppDbContext db, IFileStorage storage)
{
    public async Task<PublicProductDetailResponse> HandleAsync(
        string storeSlug,
        string productSlug,
        CancellationToken cancellationToken)
    {
        var storeId = await GetPublicCatalogueHandler.RequireVisibleStoreIdAsync(db, storeSlug, cancellationToken);
        var slug = CatalogueSlug.Normalise(productSlug);

        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.StoreId == storeId && p.Slug == slug && p.DeletedAt == null && p.IsActive,
                cancellationToken)
            ?? throw new NotFoundException("Product", slug);

        var mediaIds = product.Images.Select(i => i.MediaId).ToArray();

        var keys = mediaIds.Length == 0
            ? []
            : await db.MediaAssets
                .Where(m => mediaIds.Contains(m.Id) && m.StoreId == storeId)
                .Select(m => new { m.Id, m.StorageKey })
                .ToDictionaryAsync(m => m.Id, m => m.StorageKey, cancellationToken);

        var categoryName = product.CategoryId is { } categoryId
            ? await db.Categories
                .Where(c => c.Id == categoryId && c.IsActive && c.DeletedAt == null)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var images = product.Images
            .OrderBy(i => i.DisplayOrder)
            .Where(i => keys.ContainsKey(i.MediaId))
            .Select(i => new ProductImageResponse(i.Id, storage.GetPublicUrl(keys[i.MediaId]), i.AltText, i.DisplayOrder))
            .ToList();

        // This product's questions plus the store-wide ones, which is exactly what the order form
        // must render. Store-wide first, so a delivery date does not end up buried under
        // product-specific detail.
        var fields = await db.CustomFields
            .AsNoTracking()
            .Where(f => f.StoreId == storeId
                        && f.DeletedAt == null
                        && (f.ProductId == product.Id || f.ProductId == null))
            .OrderBy(f => f.ProductId == null ? 0 : 1)
            .ThenBy(f => f.DisplayOrder)
            .ToListAsync(cancellationToken);

        return new PublicProductDetailResponse(
            product.Slug,
            product.Name,
            product.Description,
            product.Price,
            product.PriceIsFrom,
            product.AcceptsCustomOrder,
            categoryName,
            images,
            [.. fields.Select(f => f.ToPublicResponse())]);
    }
}

/// <summary>
/// Projection row for the storefront catalogue. A named type rather than an anonymous one so it can
/// cross a method boundary without <c>dynamic</c>.
/// </summary>
internal sealed record CatalogueRow(
    string Slug,
    string Name,
    string? Description,
    decimal? Price,
    bool PriceIsFrom,
    bool AcceptsCustomOrder,
    Guid? CategoryId,
    string? PrimaryImageKey);
