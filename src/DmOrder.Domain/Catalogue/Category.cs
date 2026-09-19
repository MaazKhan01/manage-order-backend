using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Catalogue;

/// <summary>
/// A grouping the seller defines — "Cakes", "Bouquets", "Bridal", "Gift boxes".
///
/// Nothing here assumes a product category: the seller names their own groups, which is what lets one
/// platform serve a baker and a jeweller without a schema change.
/// </summary>
public sealed class Category : Entity, ITenantOwned
{
    private Category() { }

    public Guid StoreId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Soft delete, because products and historical orders reference categories. A hard delete would
    /// either cascade into order history or leave dangling references.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Category Create(Guid storeId, string name, string slug, int displayOrder)
    {
        var normalisedSlug = CatalogueSlug.Normalise(slug);

        if (!CatalogueSlug.IsValid(normalisedSlug))
        {
            throw new BusinessRuleException(CatalogueSlug.Requirements);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("A category name is required.");
        }

        return new Category
        {
            StoreId = storeId,
            Name = name.Trim(),
            Slug = normalisedSlug,
            DisplayOrder = displayOrder,
        };
    }

    public void Update(string name, string slug, bool isActive)
    {
        var normalisedSlug = CatalogueSlug.Normalise(slug);

        if (!CatalogueSlug.IsValid(normalisedSlug))
        {
            throw new BusinessRuleException(CatalogueSlug.Requirements);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("A category name is required.");
        }

        Name = name.Trim();
        Slug = normalisedSlug;
        IsActive = isActive;
    }

    public void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;

    public void Delete(DateTimeOffset now)
    {
        DeletedAt ??= now;
        IsActive = false;
    }
}
