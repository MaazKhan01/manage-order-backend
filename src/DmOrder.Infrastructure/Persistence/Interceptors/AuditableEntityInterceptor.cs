using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DmOrder.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Maintains CreatedAt / UpdatedAt so no use case has to remember to. Timestamps come from
/// <see cref="IDateTimeProvider"/> rather than DateTimeOffset.UtcNow so tests can control them.
/// </summary>
public sealed class AuditableEntityInterceptor(IDateTimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTimestamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyTimestamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void ApplyTimestamps(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
