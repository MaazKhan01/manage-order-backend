using DmOrder.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Stores;

public sealed class PublishStoreHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IDateTimeProvider clock,
    ILogger<PublishStoreHandler> logger)
{
    public async Task<StoreDetailResponse> HandleAsync(bool publish, CancellationToken cancellationToken)
    {
        var store = await StoreLoader.RequireOwnStoreAsync(db, currentUser, cancellationToken);

        if (publish)
        {
            // The domain decides whether the store is ready. Duplicating that rule here is how the two
            // drift apart.
            store.Publish(clock.UtcNow);
        }
        else
        {
            store.Unpublish();
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Store {StoreId} {Action}", store.Id, publish ? "published" : "unpublished");

        var mediaUrls = await MediaUrlResolver.ForStoreAsync(db, storage, store, cancellationToken);
        return store.ToDetail(mediaUrls);
    }
}
