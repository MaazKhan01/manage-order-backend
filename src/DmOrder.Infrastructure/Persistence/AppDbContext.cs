using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Media;
using DmOrder.Domain.Stores;
using DmOrder.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IAppDbContext
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<StoreTheme> StoreThemes => Set<StoreTheme>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Every IEntityTypeConfiguration in this assembly is picked up automatically, so adding an
        // entity never means editing this method.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // DbContext.SaveChangesAsync(CancellationToken) already satisfies IAppDbContext.
}
