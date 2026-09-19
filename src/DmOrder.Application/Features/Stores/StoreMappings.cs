using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Stores;

namespace DmOrder.Application.Features.Stores;

/// <summary>
/// Entity to DTO mapping.
///
/// Media is resolved to absolute URLs here rather than exposing storage keys, so a client never learns
/// anything about the storage layout and swapping provider changes nothing in the contract.
/// </summary>
internal static class StoreMappings
{
    public static StoreDetailResponse ToDetail(this Store store, IReadOnlyDictionary<Guid, string> mediaUrls) =>
        new(
            store.Id,
            store.Name,
            store.Slug,
            store.Description,
            store.ContactPhone,
            store.WhatsApp,
            store.ContactEmail,
            store.InstagramUrl,
            store.FacebookUrl,
            store.TiktokUrl,
            store.AddressText,
            store.City,
            store.Country,
            store.Currency,
            store.SeoTitle,
            store.SeoDescription,
            Resolve(mediaUrls, store.LogoMediaId),
            Resolve(mediaUrls, store.CoverMediaId),
            store.IsPublished,
            store.PublishedAt,
            store.IsActive,
            store.IsActive && store.HasContactChannel,
            store.Theme.ToResponse(Resolve(mediaUrls, store.Theme.BackgroundMediaId)));

    public static PublicStoreResponse ToPublic(this Store store, IReadOnlyDictionary<Guid, string> mediaUrls) =>
        new(
            store.Name,
            store.Slug,
            store.Description,
            store.ContactPhone,
            store.WhatsApp,
            store.ContactEmail,
            store.InstagramUrl,
            store.FacebookUrl,
            store.TiktokUrl,
            store.AddressText,
            store.City,
            store.Country,
            store.Currency,
            store.SeoTitle,
            store.SeoDescription,
            Resolve(mediaUrls, store.LogoMediaId),
            Resolve(mediaUrls, store.CoverMediaId),
            store.Theme.ToResponse(Resolve(mediaUrls, store.Theme.BackgroundMediaId)));

    public static StoreThemeResponse ToResponse(this StoreTheme theme, string? backgroundUrl) =>
        new(
            theme.PrimaryColor,
            theme.AccentColor,
            theme.BackgroundColor,
            theme.FontChoice.ToString(),
            theme.ButtonStyle.ToString(),
            theme.LayoutVariant.ToString(),
            backgroundUrl);

    private static string? Resolve(IReadOnlyDictionary<Guid, string> mediaUrls, Guid? mediaId) =>
        mediaId is { } id && mediaUrls.TryGetValue(id, out var url) ? url : null;
}

internal static class MediaUrlResolver
{
    /// <summary>
    /// Resolves the handful of media ids a store references in one query, rather than one per image.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ForStoreAsync(
        IAppDbContext db,
        IFileStorage storage,
        Store store,
        CancellationToken cancellationToken)
    {
        Guid[] ids =
        [
            .. new[] { store.LogoMediaId, store.CoverMediaId, store.Theme.BackgroundMediaId }
                .Where(id => id is not null)
                .Select(id => id!.Value),
        ];

        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var keys = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.MediaAssets
                .Where(m => ids.Contains(m.Id) && m.StoreId == store.Id)
                .Select(m => new { m.Id, m.StorageKey }),
            cancellationToken);

        return keys.ToDictionary(k => k.Id, k => storage.GetPublicUrl(k.StorageKey));
    }
}
