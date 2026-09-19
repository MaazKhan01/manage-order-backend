using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Catalogue;
using DmOrder.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Catalogue;

/// <summary>
/// Resolves the caller's store once, from the authenticated identity.
///
/// Every catalogue handler starts here, so "which store am I allowed to touch" is answered in exactly
/// one place and never from a route value or request body.
/// </summary>
internal static class StoreScope
{
    public static async Task<Guid> RequireStoreIdAsync(
        IAppDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var storeId = await db.Stores
            .Where(s => s.OwnerUserId == userId)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return storeId ?? throw new NotFoundException("Store", userId);
    }
}

internal static class CatalogueGuards
{
    /// <summary>
    /// Loads a category that belongs to this store, or throws not-found.
    ///
    /// The store id comes from the token and the category id from the URL, and both must match — this
    /// is what turns an IDOR attempt into a 404 rather than someone else's data.
    /// </summary>
    public static async Task<Category> RequireOwnCategoryAsync(
        IAppDbContext db,
        Guid storeId,
        Guid categoryId,
        CancellationToken cancellationToken) =>
        await db.Categories.FirstOrDefaultAsync(
            c => c.Id == categoryId && c.StoreId == storeId && c.DeletedAt == null,
            cancellationToken)
        ?? throw new NotFoundException("Category", categoryId);

    public static async Task<Product> RequireOwnProductAsync(
        IAppDbContext db,
        Guid storeId,
        Guid productId,
        CancellationToken cancellationToken) =>
        await db.Products.FirstOrDefaultAsync(
            p => p.Id == productId && p.StoreId == storeId && p.DeletedAt == null,
            cancellationToken)
        ?? throw new NotFoundException("Product", productId);

    public static async Task RequireCategorySlugAvailableAsync(
        IAppDbContext db,
        Guid storeId,
        string slug,
        Guid? exceptCategoryId,
        CancellationToken cancellationToken)
    {
        var taken = await db.Categories.AnyAsync(
            c => c.StoreId == storeId
                 && c.Slug == slug
                 && c.DeletedAt == null
                 && (exceptCategoryId == null || c.Id != exceptCategoryId),
            cancellationToken);

        if (taken)
        {
            throw new ConflictException("You already have a category with that web address.");
        }
    }

    public static async Task RequireProductSlugAvailableAsync(
        IAppDbContext db,
        Guid storeId,
        string slug,
        Guid? exceptProductId,
        CancellationToken cancellationToken)
    {
        var taken = await db.Products.AnyAsync(
            p => p.StoreId == storeId
                 && p.Slug == slug
                 && p.DeletedAt == null
                 && (exceptProductId == null || p.Id != exceptProductId),
            cancellationToken);

        if (taken)
        {
            throw new ConflictException("You already have a product with that web address.");
        }
    }

    /// <summary>
    /// Confirms a category belongs to this store before a product is filed under it.
    ///
    /// Without this a seller could move their product into another seller's category by guessing an id.
    /// </summary>
    public static async Task RequireCategoryBelongsToStoreAsync(
        IAppDbContext db,
        Guid storeId,
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId is not { } id)
        {
            return;
        }

        var belongs = await db.Categories.AnyAsync(
            c => c.Id == id && c.StoreId == storeId && c.DeletedAt == null,
            cancellationToken);

        if (!belongs)
        {
            throw new NotFoundException("Category", id);
        }
    }

    /// <summary>
    /// Confirms a product belongs to this store, when one was named at all.
    ///
    /// Used where a product id is optional — a custom field with no product is a store-wide question.
    /// </summary>
    public static async Task RequireCategoryOrProductAsync(
        IAppDbContext db,
        Guid storeId,
        Guid? productId,
        CancellationToken cancellationToken)
    {
        if (productId is not { } id)
        {
            return;
        }

        var belongs = await db.Products.AnyAsync(
            p => p.Id == id && p.StoreId == storeId && p.DeletedAt == null,
            cancellationToken);

        if (!belongs)
        {
            throw new NotFoundException("Product", id);
        }
    }

    /// <summary>Confirms an uploaded image belongs to this store before it is attached to a product.</summary>
    public static async Task RequireMediaBelongsToStoreAsync(
        IAppDbContext db,
        Guid storeId,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var belongs = await db.MediaAssets.AnyAsync(
            m => m.Id == mediaId && m.StoreId == storeId,
            cancellationToken);

        if (!belongs)
        {
            throw new NotFoundException("Image", mediaId);
        }
    }
}
