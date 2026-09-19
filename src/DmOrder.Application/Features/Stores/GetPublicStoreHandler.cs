using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Stores;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Stores;

/// <summary>
/// The anonymous storefront read.
///
/// Note what this does NOT do: it never touches <see cref="IStoreContext"/>. The public path resolves
/// a store from a published slug, and the authenticated path resolves one from an identity. Keeping
/// the two resolvers apart is what stops an anonymous request ever being treated as a seller's.
/// </summary>
public sealed class GetPublicStoreHandler(IAppDbContext db, IFileStorage storage)
{
    public async Task<PublicStoreResponse> HandleAsync(string slug, CancellationToken cancellationToken)
    {
        var normalised = slug.Trim().ToLowerInvariant();

        var store = await db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Slug == normalised && s.IsPublished && s.IsActive,
                cancellationToken);

        // An unpublished or suspended store is indistinguishable from one that never existed. Saying
        // "this exists but is hidden" would leak that a seller is preparing something.
        if (store is null)
        {
            throw new NotFoundException("Store", normalised);
        }

        var mediaUrls = await MediaUrlResolver.ForStoreAsync(db, storage, store, cancellationToken);
        return store.ToPublic(mediaUrls);
    }
}

public sealed class CheckSlugAvailabilityHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// Availability for the store-setup form.
    ///
    /// This is anonymous, so it does reveal whether a given slug is taken — which is unavoidable, since
    /// every taken slug is already a public URL anyone can visit. It reveals nothing that browsing the
    /// site would not.
    /// </summary>
    public async Task<SlugAvailabilityResponse> HandleAsync(string? slug, CancellationToken cancellationToken)
    {
        var normalised = (slug ?? string.Empty).Trim().ToLowerInvariant();

        var validation = StoreSlug.Validate(normalised);
        if (!validation.IsValid)
        {
            return new SlugAvailabilityResponse(normalised, false, validation.Error);
        }

        var ownerUserId = currentUser.UserId;

        // A seller checking their own current slug should be told it is available, otherwise the edit
        // form reports a conflict with itself.
        var takenByAnother = await db.Stores.AnyAsync(
            s => s.Slug == normalised && (ownerUserId == null || s.OwnerUserId != ownerUserId),
            cancellationToken);

        return takenByAnother
            ? new SlugAvailabilityResponse(normalised, false, "That address is already taken.")
            : new SlugAvailabilityResponse(normalised, true, null);
    }
}
