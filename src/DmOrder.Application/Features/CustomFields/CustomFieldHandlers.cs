using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.Catalogue;
using DmOrder.Domain.CustomFields;
using DmOrder.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.CustomFields;

public sealed class ListCustomFieldsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// The seller's questions. Filtered to one product when asked, otherwise everything including
    /// the store-wide ones.
    /// </summary>
    public async Task<IReadOnlyList<CustomFieldResponse>> HandleAsync(
        Guid? productId,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var query = db.CustomFields
            .AsNoTracking()
            .Where(f => f.StoreId == storeId && f.DeletedAt == null);

        if (productId is { } id)
        {
            // Store-wide fields are included: they are asked on this product's form too.
            query = query.Where(f => f.ProductId == id || f.ProductId == null);
        }

        var fields = await query
            .OrderBy(f => f.ProductId == null ? 0 : 1)
            .ThenBy(f => f.DisplayOrder)
            .ToListAsync(cancellationToken);

        var productNames = await db.Products
            .Where(p => p.StoreId == storeId)
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        return [.. fields.Select(f => f.ToResponse(
            f.ProductId is { } pid && productNames.TryGetValue(pid, out var name) ? name : null))];
    }
}

public sealed class CreateCustomFieldHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<CustomFieldResponse> HandleAsync(
        CreateCustomFieldRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        // A product id from the client must be proven to belong to this store, or a seller could
        // attach questions to someone else's product.
        await CatalogueGuards.RequireCategoryOrProductAsync(db, storeId, request.ProductId, cancellationToken);

        var nextOrder = await db.CustomFields
            .Where(f => f.StoreId == storeId && f.ProductId == request.ProductId && f.DeletedAt == null)
            .Select(f => (int?)f.DisplayOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var field = CustomField.Create(storeId, request.ProductId, request.Label, request.FieldType, nextOrder + 1);

        // A choice question with no options cannot be answered, so it starts with a sensible pair the
        // seller then edits. Shipping an unanswerable question would be worse than presumptuous.
        if (field.RequiresOptions)
        {
            field.ReplaceOptions([("Option 1", null), ("Option 2", null)]);
        }

        db.CustomFields.Add(field);
        await db.SaveChangesAsync(cancellationToken);

        return field.ToResponse(null);
    }
}

public sealed class UpdateCustomFieldHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<CustomFieldResponse> HandleAsync(
        Guid fieldId,
        UpdateCustomFieldRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var field = await RequireOwnFieldAsync(db, storeId, fieldId, cancellationToken);

        field.Update(
            request.Label,
            request.HelpText,
            request.FieldType,
            request.IsRequired,
            request.MinValue,
            request.MaxValue,
            request.MaxLength);

        field.ReplaceOptions([.. request.Options.Select(o => (o.Label, o.Value))]);

        await db.SaveChangesAsync(cancellationToken);

        return field.ToResponse(null);
    }

    internal static async Task<CustomField> RequireOwnFieldAsync(
        IAppDbContext db,
        Guid storeId,
        Guid fieldId,
        CancellationToken cancellationToken) =>
        await db.CustomFields.FirstOrDefaultAsync(
            f => f.Id == fieldId && f.StoreId == storeId && f.DeletedAt == null,
            cancellationToken)
        ?? throw new NotFoundException("Question", fieldId);
}

public sealed class DeleteCustomFieldHandler(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
{
    public async Task HandleAsync(Guid fieldId, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var field = await UpdateCustomFieldHandler.RequireOwnFieldAsync(db, storeId, fieldId, cancellationToken);

        // Soft delete. Orders snapshot the label and type, so past answers stay readable either way,
        // but keeping the row lets analytics still group by the question.
        field.Delete(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ReorderCustomFieldsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task HandleAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);

        var fields = await db.CustomFields
            .Where(f => f.StoreId == storeId && f.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var order = 0;
        foreach (var id in idsInOrder)
        {
            var field = fields.FirstOrDefault(f => f.Id == id)
                ?? throw new NotFoundException("Question", id);

            field.SetDisplayOrder(order++);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class CustomFieldMappings
{
    public static CustomFieldResponse ToResponse(this CustomField field, string? productName) =>
        new(
            field.Id,
            field.ProductId,
            productName,
            field.Label,
            field.HelpText,
            field.FieldType.ToString(),
            field.IsRequired,
            field.DisplayOrder,
            field.MinValue,
            field.MaxValue,
            field.MaxLength,
            [.. field.Options.OrderBy(o => o.DisplayOrder)
                .Select(o => new CustomFieldOptionResponse(o.Id, o.Label, o.Value, o.DisplayOrder))]);

    public static PublicCustomFieldResponse ToPublicResponse(this CustomField field) =>
        new(
            field.Id,
            field.Label,
            field.HelpText,
            field.FieldType.ToString(),
            field.IsRequired,
            field.MinValue,
            field.MaxValue,
            field.MaxLength,
            [.. field.Options.OrderBy(o => o.DisplayOrder).Select(o => o.Value)]);
}

public sealed class CreateCustomFieldValidator : AbstractValidator<CreateCustomFieldRequest>
{
    public CreateCustomFieldValidator()
    {
        RuleFor(x => x.Label).NotEmpty().WithMessage("A question is required.").MaximumLength(160);
        RuleFor(x => x.FieldType).IsInEnum().WithMessage("Choose one of the available answer types.");
    }
}

public sealed class UpdateCustomFieldValidator : AbstractValidator<UpdateCustomFieldRequest>
{
    public UpdateCustomFieldValidator()
    {
        RuleFor(x => x.Label).NotEmpty().WithMessage("A question is required.").MaximumLength(160);
        RuleFor(x => x.HelpText).MaximumLength(500);
        RuleFor(x => x.FieldType).IsInEnum().WithMessage("Choose one of the available answer types.");

        RuleFor(x => x.MaxLength)
            .GreaterThan(0).When(x => x.MaxLength.HasValue)
            .WithMessage("The maximum length must be at least 1.");

        RuleFor(x => x)
            .Must(x => x.MinValue is null || x.MaxValue is null || x.MinValue <= x.MaxValue)
            .WithMessage("The smallest value cannot be larger than the largest.")
            .WithName(nameof(UpdateCustomFieldRequest.MinValue));

        RuleFor(x => x.Options)
            .NotEmpty()
            .When(x => x.FieldType is CustomFieldType.Select or CustomFieldType.MultiSelect)
            .WithMessage("A choice question needs at least one option.");

        RuleForEach(x => x.Options).ChildRules(option =>
            option.RuleFor(o => o.Label).NotEmpty().WithMessage("Every option needs a label.").MaximumLength(160));
    }
}
