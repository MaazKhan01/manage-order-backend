using DmOrder.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    // DbSets are added here as entities are introduced (store, products, orders, ...).

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Every IEntityTypeConfiguration in this assembly is picked up automatically, so adding an
        // entity never means editing this method.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // DbContext.SaveChangesAsync(CancellationToken) already satisfies IAppDbContext.
}
