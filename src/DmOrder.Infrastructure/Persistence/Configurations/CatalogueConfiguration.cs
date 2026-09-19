using DmOrder.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmOrder.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(CatalogueSlug.MaxLength).IsRequired();
        builder.Property(c => c.IsActive).HasDefaultValue(true);

        // Unique per store, not globally — two sellers may both have "cakes".
        //
        // Filtered on DeletedAt so a soft-deleted category does not permanently reserve its slug: a
        // seller who deletes "Cakes" by mistake must be able to recreate it.
        builder.HasIndex(c => new { c.StoreId, c.Slug })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        builder.HasIndex(c => new { c.StoreId, c.DisplayOrder });
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(160).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(CatalogueSlug.MaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(4000);

        // Exact decimal. Money must never go near a floating-point type.
        builder.Property(p => p.Price).HasColumnType("numeric(12,2)");

        builder.Property(p => p.IsActive).HasDefaultValue(true);
        builder.Property(p => p.AcceptsCustomOrder).HasDefaultValue(true);

        builder.HasIndex(p => new { p.StoreId, p.Slug })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        // The storefront listing: a store's active products, grouped and ordered.
        builder.HasIndex(p => new { p.StoreId, p.CategoryId, p.DisplayOrder });

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            // Deleting a category orphans its products rather than destroying them. A seller
            // reorganising their catalogue must not lose the work.
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Images)
            .WithOne()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Images).AutoInclude();
    }
}

public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_images");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.AltText).HasMaxLength(200);

        builder.HasIndex(i => new { i.ProductId, i.DisplayOrder });

        // One media asset belongs to one product slot; re-adding the same image is a no-op upstream.
        builder.HasIndex(i => new { i.ProductId, i.MediaId }).IsUnique();
    }
}
