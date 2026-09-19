using DmOrder.Domain.Stores;

namespace DmOrder.Application.Features.Stores;

// --- Requests -----------------------------------------------------------

public sealed record CreateStoreRequest(string Name, string Slug, string? Currency);

public sealed record UpdateStoreProfileRequest(
    string Name,
    string? Description,
    string? ContactPhone,
    string? WhatsApp,
    string? ContactEmail,
    string? InstagramUrl,
    string? FacebookUrl,
    string? TiktokUrl,
    string? AddressText,
    string? City,
    string? Country,
    string? Currency,
    string? SeoTitle,
    string? SeoDescription);

public sealed record ChangeStoreSlugRequest(string Slug);

public sealed record UpdateStoreThemeRequest(
    string PrimaryColor,
    string AccentColor,
    string BackgroundColor,
    StoreFontChoice FontChoice,
    StoreButtonStyle ButtonStyle,
    StoreLayoutVariant LayoutVariant);

public sealed record SetStoreImageRequest(Guid? MediaId);

// --- Responses ----------------------------------------------------------

/// <summary>The seller's own view of their store. Includes unpublished state and admin flags.</summary>
public sealed record StoreDetailResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? ContactPhone,
    string? WhatsApp,
    string? ContactEmail,
    string? InstagramUrl,
    string? FacebookUrl,
    string? TiktokUrl,
    string? AddressText,
    string? City,
    string? Country,
    string Currency,
    string? SeoTitle,
    string? SeoDescription,
    string? LogoUrl,
    string? CoverUrl,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    bool IsActive,
    bool CanPublish,
    StoreThemeResponse Theme);

public sealed record StoreThemeResponse(
    string PrimaryColor,
    string AccentColor,
    string BackgroundColor,
    string FontChoice,
    string ButtonStyle,
    string LayoutVariant,
    string? BackgroundUrl);

/// <summary>
/// What an anonymous visitor sees. Deliberately a different shape from the seller's view: no admin
/// flags, no SEO drafts, no publishing state — a storefront that is not visible simply 404s.
/// </summary>
public sealed record PublicStoreResponse(
    string Name,
    string Slug,
    string? Description,
    string? ContactPhone,
    string? WhatsApp,
    string? ContactEmail,
    string? InstagramUrl,
    string? FacebookUrl,
    string? TiktokUrl,
    string? AddressText,
    string? City,
    string? Country,
    string Currency,
    string? SeoTitle,
    string? SeoDescription,
    string? LogoUrl,
    string? CoverUrl,
    StoreThemeResponse Theme);

public sealed record SlugAvailabilityResponse(string Slug, bool IsAvailable, string? Reason);

public sealed record StoreSuggestionResponse(string Slug);
