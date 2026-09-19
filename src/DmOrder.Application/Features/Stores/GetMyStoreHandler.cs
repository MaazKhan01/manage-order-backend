using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Stores;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Features.Stores;

public sealed class GetMyStoreHandler(IAppDbContext db, ICurrentUser currentUser, IFileStorage storage)
{
    /// <summary>Returns the caller's store, or null when they have not created one yet.</summary>
    public async Task<StoreDetailResponse?> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        // Scoped by owner, not by an id from the request. There is no way to ask for someone else's.
        var store = await db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerUserId == userId, cancellationToken);

        if (store is null)
        {
            return null;
        }

        var mediaUrls = await MediaUrlResolver.ForStoreAsync(db, storage, store, cancellationToken);
        return store.ToDetail(mediaUrls);
    }
}

/// <summary>
/// Loads the caller's store for a write, tracked.
///
/// Shared by every seller-side store command so the "which store am I allowed to touch" question is
/// answered in exactly one place, by owner id from the authenticated identity.
/// </summary>
internal static class StoreLoader
{
    public static async Task<Store> RequireOwnStoreAsync(
        IAppDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        return await db.Stores.FirstOrDefaultAsync(s => s.OwnerUserId == userId, cancellationToken)
            ?? throw new NotFoundException("Store", userId);
    }
}
