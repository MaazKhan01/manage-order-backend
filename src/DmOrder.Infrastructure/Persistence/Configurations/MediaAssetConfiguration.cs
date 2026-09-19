using DmOrder.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmOrder.Infrastructure.Persistence.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(m => m.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(m => m.Purpose).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.HasIndex(m => m.StoreId);
        builder.HasIndex(m => new { m.StoreId, m.Purpose });

        // Every media row belongs to exactly one store, which is what makes the tenant guard able to
        // reject a cross-tenant write.
        builder.HasIndex(m => m.StorageKey).IsUnique();
    }
}
