namespace DmOrder.Application.Features.Catalogue;

// --- Categories ---------------------------------------------------------

public sealed record CreateCategoryRequest(string Name, string? Slug);

public sealed record UpdateCategoryRequest(string Name, string Slug, bool IsActive);

public sealed record ReorderRequest(IReadOnlyList<Guid> IdsInOrder);

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    int DisplayOrder,
    bool IsActive,
    int ProductCount);

// --- Products -----------------------------------------------------------

public sealed record CreateProductRequest(string Name, string? Slug, Guid? CategoryId);

public sealed record UpdateProductRequest(
    string Name,
    string Slug,
    string? Description,
    decimal? Price,
    bool PriceIsFrom,
    Guid? CategoryId,
    bool IsActive,
    bool AcceptsCustomOrder);

public sealed record AddProductImageRequest(Guid MediaId, string? AltText);

public sealed record ProductImageResponse(Guid Id, string Url, string? AltText, int DisplayOrder);

/// <summary>Row shape for the seller's product list. Deliberately lighter than the detail view.</summary>
public sealed record ProductListItemResponse(
    Guid Id,
    string Name,
    string Slug,
    decimal? Price,
    bool PriceIsFrom,
    bool IsActive,
    int DisplayOrder,
    Guid? CategoryId,
    string? CategoryName,
    string? PrimaryImageUrl,
    int ImageCount);

public sealed record ProductDetailResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    decimal? Price,
    bool PriceIsFrom,
    bool IsActive,
    bool AcceptsCustomOrder,
    int DisplayOrder,
    Guid? CategoryId,
    string? CategoryName,
    IReadOnlyList<ProductImageResponse> Images);

// --- Public storefront --------------------------------------------------

/// <summary>
/// What an anonymous visitor sees. No inactive rows, no display-order internals, no ids a seller
/// would use for management.
/// </summary>
public sealed record PublicProductResponse(
    string Slug,
    string Name,
    string? Description,
    decimal? Price,
    bool PriceIsFrom,
    bool AcceptsCustomOrder,
    string? PrimaryImageUrl);

public sealed record PublicCategoryResponse(
    string Slug,
    string Name,
    IReadOnlyList<PublicProductResponse> Products);

/// <summary>
/// The storefront catalogue: categories in the seller's order, plus anything uncategorised.
/// One payload, because a storefront renders it all at once.
/// </summary>
public sealed record PublicCatalogueResponse(
    IReadOnlyList<PublicCategoryResponse> Categories,
    IReadOnlyList<PublicProductResponse> Uncategorised,
    int TotalProducts);

public sealed record PublicProductDetailResponse(
    string Slug,
    string Name,
    string? Description,
    decimal? Price,
    bool PriceIsFrom,
    bool AcceptsCustomOrder,
    string? CategoryName,
    IReadOnlyList<ProductImageResponse> Images);
