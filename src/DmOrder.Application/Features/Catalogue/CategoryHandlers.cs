using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.Stores;
using DmOrder.Domain.Catalogue;
using DmOrder.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Catalogue;

public sealed class ListCategoriesHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<CategoryResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        // Projected in the query: the product count never loads a product.
        return await db.Categories
            .AsNoTracking()
            .Where(c => c.StoreId == storeId && c.DeletedAt == null)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryResponse(
                c.Id,
                c.Name,
                c.Slug,
                c.DisplayOrder,
                c.IsActive,
                db.Products.Count(p => p.CategoryId == c.Id && p.DeletedAt == null)))
            .ToListAsync(cancellationToken);
    }
}

public sealed class CreateCategoryHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<CategoryResponse> HandleAsync(
        CreateCategoryRequest request,
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

        await CatalogueGuards.RequireCategorySlugAvailableAsync(db, storeId, slug, null, cancellationToken);

        var nextOrder = await db.Categories
            .Where(c => c.StoreId == storeId && c.DeletedAt == null)
            .Select(c => (int?)c.DisplayOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var category = Category.Create(storeId, request.Name, slug, nextOrder + 1);

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return new CategoryResponse(category.Id, category.Name, category.Slug, category.DisplayOrder, category.IsActive, 0);
    }
}

public sealed class UpdateCategoryHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<CategoryResponse> HandleAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var category = await CatalogueGuards.RequireOwnCategoryAsync(db, storeId, categoryId, cancellationToken);

        var slug = CatalogueSlug.Normalise(request.Slug);
        await CatalogueGuards.RequireCategorySlugAvailableAsync(db, storeId, slug, categoryId, cancellationToken);

        category.Update(request.Name, slug, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        var productCount = await db.Products
            .CountAsync(p => p.CategoryId == category.Id && p.DeletedAt == null, cancellationToken);

        return new CategoryResponse(
            category.Id, category.Name, category.Slug, category.DisplayOrder, category.IsActive, productCount);
    }
}

public sealed class DeleteCategoryHandler(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
{
    public async Task HandleAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var category = await CatalogueGuards.RequireOwnCategoryAsync(db, storeId, categoryId, cancellationToken);

        // Products are orphaned, not deleted. A seller reorganising their catalogue must not lose
        // products as a side effect of tidying up.
        var products = await db.Products
            .Where(p => p.StoreId == storeId && p.CategoryId == categoryId)
            .ToListAsync(cancellationToken);

        foreach (var product in products)
        {
            product.ClearCategory();
        }

        category.Delete(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ReorderCategoriesHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task HandleAsync(ReorderRequest request, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var categories = await db.Categories
            .Where(c => c.StoreId == storeId && c.DeletedAt == null)
            .ToListAsync(cancellationToken);

        // Scoped to the caller's store, so an id belonging to another seller simply is not found here
        // — the reorder cannot be used to probe for or touch someone else's rows.
        var order = 0;
        foreach (var id in request.IdsInOrder)
        {
            var category = categories.FirstOrDefault(c => c.Id == id)
                ?? throw new NotFoundException("Category", id);

            category.SetDisplayOrder(order++);
        }

        foreach (var remaining in categories
            .Where(c => !request.IdsInOrder.Contains(c.Id))
            .OrderBy(c => c.DisplayOrder))
        {
            remaining.SetDisplayOrder(order++);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("A category name is required.").MaximumLength(120);
        RuleFor(x => x.Slug).MaximumLength(CatalogueSlug.MaxLength);
    }
}

public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("A category name is required.").MaximumLength(120);
        RuleFor(x => x.Slug)
            .NotEmpty()
            .Must(slug => CatalogueSlug.IsValid(CatalogueSlug.Normalise(slug)))
            .WithMessage(CatalogueSlug.Requirements);
    }
}

public sealed class ReorderValidator : AbstractValidator<ReorderRequest>
{
    public ReorderValidator()
    {
        RuleFor(x => x.IdsInOrder).NotNull();

        RuleFor(x => x.IdsInOrder)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The same item appears more than once.");
    }
}
