using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Catalogue;

/// <summary>
/// Something a seller offers. Deliberately neutral: a cake, a dress, a bouquet, a photo session.
///
/// Anything specific to a trade is expressed through custom fields (Phase 5), never here.
/// </summary>
public sealed class Product : Entity, ITenantOwned
{
    private readonly List<ProductImage> _images = [];

    private Product() { }

    public Guid StoreId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>
    /// Nullable on purpose. Custom work often has no fixed price until the details are known, and
    /// forcing a number would make sellers invent one.
    /// </summary>
    public decimal? Price { get; private set; }

    /// <summary>Renders the price as "from ₨3,000" — how custom work is actually quoted.</summary>
    public bool PriceIsFrom { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int DisplayOrder { get; private set; }

    /// <summary>Whether the public order form is offered for this product.</summary>
    public bool AcceptsCustomOrder { get; private set; } = true;

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public IReadOnlyList<ProductImage> Images => _images;

    public static Product Create(
        Guid storeId,
        string name,
        string slug,
        Guid? categoryId,
        int displayOrder)
    {
        var normalisedSlug = CatalogueSlug.Normalise(slug);

        if (!CatalogueSlug.IsValid(normalisedSlug))
        {
            throw new BusinessRuleException(CatalogueSlug.Requirements);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("A product name is required.");
        }

        return new Product
        {
            StoreId = storeId,
            Name = name.Trim(),
            Slug = normalisedSlug,
            CategoryId = categoryId,
            DisplayOrder = displayOrder,
        };
    }

    public void Update(
        string name,
        string slug,
        string? description,
        decimal? price,
        bool priceIsFrom,
        Guid? categoryId,
        bool isActive,
        bool acceptsCustomOrder)
    {
        var normalisedSlug = CatalogueSlug.Normalise(slug);

        if (!CatalogueSlug.IsValid(normalisedSlug))
        {
            throw new BusinessRuleException(CatalogueSlug.Requirements);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("A product name is required.");
        }

        if (price is < 0)
        {
            throw new BusinessRuleException("A price cannot be negative.");
        }

        // "From" only means something alongside a number. Allowing it without one would render as a
        // bare "from" on the storefront.
        if (priceIsFrom && price is null)
        {
            throw new BusinessRuleException("Set a starting price, or turn off \"price starts from\".");
        }

        Name = name.Trim();
        Slug = normalisedSlug;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = price;
        PriceIsFrom = priceIsFrom;
        CategoryId = categoryId;
        IsActive = isActive;
        AcceptsCustomOrder = acceptsCustomOrder;
    }

    public void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;

    public void Delete(DateTimeOffset now)
    {
        DeletedAt ??= now;
        IsActive = false;
    }

    /// <summary>Called when a category is removed, so products are orphaned rather than deleted.</summary>
    public void ClearCategory() => CategoryId = null;

    public void AddImage(Guid mediaId, string? altText)
    {
        if (_images.Count >= MaxImages)
        {
            throw new BusinessRuleException($"A product can have at most {MaxImages} images.");
        }

        if (_images.Any(image => image.MediaId == mediaId))
        {
            return;
        }

        var nextOrder = _images.Count == 0 ? 0 : _images.Max(image => image.DisplayOrder) + 1;
        _images.Add(ProductImage.Create(Id, mediaId, nextOrder, altText));
    }

    public void RemoveImage(Guid imageId)
    {
        var image = _images.FirstOrDefault(candidate => candidate.Id == imageId);

        if (image is null)
        {
            throw new NotFoundException("Image", imageId);
        }

        _images.Remove(image);
        Resequence();
    }

    /// <summary>
    /// Reorders images to the given sequence. Ids not belonging to this product are rejected rather
    /// than ignored, so a mistake is visible instead of silently doing half the job.
    /// </summary>
    public void ReorderImages(IReadOnlyList<Guid> imageIdsInOrder)
    {
        foreach (var imageId in imageIdsInOrder)
        {
            if (_images.All(image => image.Id != imageId))
            {
                throw new NotFoundException("Image", imageId);
            }
        }

        var order = 0;
        foreach (var imageId in imageIdsInOrder)
        {
            _images.First(image => image.Id == imageId).SetDisplayOrder(order++);
        }

        // Anything the caller did not mention keeps its relative position, after the ones it did.
        foreach (var image in _images.Where(i => !imageIdsInOrder.Contains(i.Id)).OrderBy(i => i.DisplayOrder))
        {
            image.SetDisplayOrder(order++);
        }
    }

    private void Resequence()
    {
        var order = 0;
        foreach (var image in _images.OrderBy(image => image.DisplayOrder))
        {
            image.SetDisplayOrder(order++);
        }
    }

    public const int MaxImages = 8;
}

public sealed class ProductImage : Entity
{
    private ProductImage() { }

    public Guid ProductId { get; private set; }

    public Guid MediaId { get; private set; }

    public int DisplayOrder { get; private set; }

    public string? AltText { get; private set; }

    internal static ProductImage Create(Guid productId, Guid mediaId, int displayOrder, string? altText) =>
        new()
        {
            ProductId = productId,
            MediaId = mediaId,
            DisplayOrder = displayOrder,
            AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
        };

    internal void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;
}
