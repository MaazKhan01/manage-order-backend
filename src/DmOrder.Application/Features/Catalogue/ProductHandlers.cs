using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Domain.Catalogue;
using DmOrder.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Catalogue;

public sealed record ProductListQuery(int? Page, int? PageSize, Guid? CategoryId, string? Search, bool? IsActive);

public sealed class ListProductsHandler(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    public async Task<PagedResult<ProductListItemResponse>> HandleAsync(
        ProductListQuery query,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var products = db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == storeId && p.DeletedAt == null);

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (query.IsActive is { } isActive)
        {
            products = products.Where(p => p.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Provider-agnostic: EF.Functions.ILike is Npgsql-only and Application does not reference
            // the provider. Lowercasing both sides gives the same case-insensitive match.
            //
            // Wildcards are escaped so a search for "50%" means what it says rather than matching
            // everything. This is a sequential scan over one seller's own products, which is fine at
            // this size; it needs a trigram index only if catalogues get large.
            var pattern = SearchPattern.Contains(query.Search);
            products = products.Where(p => EF.Functions.Like(p.Name.ToLower(), pattern, SearchPattern.EscapeCharacter));
        }

        // Ordered before paging — an unordered page is a non-deterministic page.
        var ordered = products.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name);

        var page = new PageRequest(query.Page, query.PageSize);

        var rows = await ordered
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Price,
                p.PriceIsFrom,
                p.IsActive,
                p.DisplayOrder,
                p.CategoryId,
                CategoryName = db.Categories
                    .Where(c => c.Id == p.CategoryId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),
                // Only the first image's key is fetched, not every image of every product.
                PrimaryImageKey = db.ProductImages
                    .Where(i => i.ProductId == p.Id)
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => db.MediaAssets.Where(m => m.Id == i.MediaId).Select(m => m.StorageKey).FirstOrDefault())
                    .FirstOrDefault(),
                ImageCount = db.ProductImages.Count(i => i.ProductId == p.Id),
            })
            .ToPagedResultAsync(page, cancellationToken);

        var items = rows.Items
            .Select(r => new ProductListItemResponse(
                r.Id, r.Name, r.Slug, r.Price, r.PriceIsFrom, r.IsActive, r.DisplayOrder,
                r.CategoryId, r.CategoryName,
                r.PrimaryImageKey is null ? null : storage.GetPublicUrl(r.PrimaryImageKey),
                r.ImageCount))
            .ToList();

        return new PagedResult<ProductListItemResponse>(items, rows.Page, rows.PageSize, rows.TotalCount);
    }
}

public sealed class GetProductHandler(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    public async Task<ProductDetailResponse> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var product = await CatalogueGuards.RequireOwnProductAsync(db, storeId, productId, cancellationToken);

        return await ProductMappings.ToDetailAsync(db, storage, product, cancellationToken);
    }
}

public sealed class CreateProductHandler(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    public async Task<ProductDetailResponse> HandleAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var slug = CatalogueSlug.Normalise(
            string.IsNullOrWhiteSpace(request.Slug) ? CatalogueSlug.Suggest(request.Name) : request.Slug);

        if (!CatalogueSlug.IsValid(slug))
        {
            throw new BusinessRuleException(
                "That name cannot be turned into a web address. Add some letters or numbers.");
        }

        await CatalogueGuards.RequireProductSlugAvailableAsync(db, storeId, slug, null, cancellationToken);
        await CatalogueGuards.RequireCategoryBelongsToStoreAsync(db, storeId, request.CategoryId, cancellationToken);

        var nextOrder = await db.Products
            .Where(p => p.StoreId == storeId && p.DeletedAt == null)
            .Select(p => (int?)p.DisplayOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var product = Product.Create(storeId, request.Name, slug, request.CategoryId, nextOrder + 1);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return await ProductMappings.ToDetailAsync(db, storage, product, cancellationToken);
    }
}

public sealed class UpdateProductHandler(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    public async Task<ProductDetailResponse> HandleAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var product = await CatalogueGuards.RequireOwnProductAsync(db, storeId, productId, cancellationToken);

        var slug = CatalogueSlug.Normalise(request.Slug);
        await CatalogueGuards.RequireProductSlugAvailableAsync(db, storeId, slug, productId, cancellationToken);
        await CatalogueGuards.RequireCategoryBelongsToStoreAsync(db, storeId, request.CategoryId, cancellationToken);

        product.Update(
            request.Name,
            slug,
            request.Description,
            request.Price,
            request.PriceIsFrom,
            request.CategoryId,
            request.IsActive,
            request.AcceptsCustomOrder);

        await db.SaveChangesAsync(cancellationToken);

        return await ProductMappings.ToDetailAsync(db, storage, product, cancellationToken);
    }
}

public sealed class DeleteProductHandler(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
{
    public async Task HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var product = await CatalogueGuards.RequireOwnProductAsync(db, storeId, productId, cancellationToken);

        // Soft delete: orders placed for this product must keep rendering, and Phase 5 attaches
        // custom fields here too.
        product.Delete(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ReorderProductsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task HandleAsync(ReorderRequest request, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var products = await db.Products
            .Where(p => p.StoreId == storeId && p.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var order = 0;
        foreach (var id in request.IdsInOrder)
        {
            var product = products.FirstOrDefault(p => p.Id == id)
                ?? throw new NotFoundException("Product", id);

            product.SetDisplayOrder(order++);
        }

        foreach (var remaining in products
            .Where(p => !request.IdsInOrder.Contains(p.Id))
            .OrderBy(p => p.DisplayOrder))
        {
            remaining.SetDisplayOrder(order++);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("A product name is required.").MaximumLength(160);
        RuleFor(x => x.Slug).MaximumLength(CatalogueSlug.MaxLength);
    }
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("A product name is required.").MaximumLength(160);

        RuleFor(x => x.Slug)
            .NotEmpty()
            .Must(slug => CatalogueSlug.IsValid(CatalogueSlug.Normalise(slug)))
            .WithMessage(CatalogueSlug.Requirements);

        RuleFor(x => x.Description).MaximumLength(4000);

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).When(x => x.Price.HasValue)
            .WithMessage("A price cannot be negative.")
            // numeric(12,2) — anything larger is a typo, not a price.
            .LessThan(1_000_000_000m).When(x => x.Price.HasValue)
            .WithMessage("That price is too large.");

        RuleFor(x => x.Price)
            .NotNull().When(x => x.PriceIsFrom)
            .WithMessage("Set a starting price, or turn off \"price starts from\".");
    }
}

public sealed class AddProductImageValidator : AbstractValidator<AddProductImageRequest>
{
    public AddProductImageValidator()
    {
        RuleFor(x => x.MediaId).NotEmpty();
        RuleFor(x => x.AltText).MaximumLength(200);
    }
}
