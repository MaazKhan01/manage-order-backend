using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DmOrder.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Last line of defence against cross-tenant writes.
///
/// When the request is made by an authenticated seller, every <see cref="ITenantOwned"/> row being
/// inserted, updated or deleted must belong to that seller's store, and an existing row's StoreId may
/// never change. A violation means a use case forgot to scope a query, so it throws rather than filters.
///
/// It intentionally does nothing when there is no seller context (anonymous storefront order
/// submission, background work). Those paths are guarded in their handlers, because the correct store
/// there comes from a published store slug, not from an identity. This limitation is covered by the
/// tenant-isolation integration tests.
/// </summary>
public sealed class TenantGuardInterceptor(IStoreContext storeContext) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Guard(DbContext? context)
    {
        if (context is null || storeContext.StoreId is not { } currentStoreId)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity.StoreId != currentStoreId)
            {
                throw new ForbiddenException(
                    $"Attempted to write {entry.Entity.GetType().Name} belonging to another store.");
            }

            if (entry.State is EntityState.Modified)
            {
                var storeIdProperty = entry.Property(nameof(ITenantOwned.StoreId));
                if (storeIdProperty.IsModified)
                {
                    throw new ForbiddenException(
                        $"{entry.Entity.GetType().Name}.StoreId is immutable and cannot be reassigned.");
                }
            }
        }
    }
}
