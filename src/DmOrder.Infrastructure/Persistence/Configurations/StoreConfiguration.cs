using DmOrder.Domain.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmOrder.Infrastructure.Persistence.Configurations;

public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(120).IsRequired();

        builder.Property(s => s.Slug).HasMaxLength(StoreSlug.MaxLength).IsRequired();

        // Slugs are stored lowercase, so a plain unique index is enough and stays index-only. The
        // domain lowercases on the way in; this constraint is what actually guarantees it.
        builder.HasIndex(s => s.Slug).IsUnique();

        // One store per seller in V1. Dropping this index is the whole of "support multiple stores".
        builder.HasIndex(s => s.OwnerUserId).IsUnique();

        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.ContactPhone).HasMaxLength(32);
        builder.Property(s => s.WhatsApp).HasMaxLength(32);
        builder.Property(s => s.ContactEmail).HasMaxLength(256);
        builder.Property(s => s.InstagramUrl).HasMaxLength(500);
        builder.Property(s => s.FacebookUrl).HasMaxLength(500);
        builder.Property(s => s.TiktokUrl).HasMaxLength(500);
        builder.Property(s => s.AddressText).HasMaxLength(500);
        builder.Property(s => s.City).HasMaxLength(120);
        builder.Property(s => s.Country).HasMaxLength(120);
        builder.Property(s => s.SeoTitle).HasMaxLength(120);
        builder.Property(s => s.SeoDescription).HasMaxLength(320);
        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();

        builder.Property(s => s.IsPublished).HasDefaultValue(false);
        builder.Property(s => s.IsActive).HasDefaultValue(true);

        // The storefront lookup is "find a visible store by slug", so both flags belong in the index.
        builder.HasIndex(s => new { s.Slug, s.IsPublished, s.IsActive });

        builder.HasOne(s => s.Theme)
            .WithOne()
            .HasForeignKey<StoreTheme>(t => t.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Theme).AutoInclude();
    }
}

public sealed class StoreThemeConfiguration : IEntityTypeConfiguration<StoreTheme>
{
    public void Configure(EntityTypeBuilder<StoreTheme> builder)
    {
        builder.ToTable("store_themes");

        builder.HasKey(t => t.StoreId);

        builder.Property(t => t.PrimaryColor).HasMaxLength(7).IsRequired();
        builder.Property(t => t.AccentColor).HasMaxLength(7).IsRequired();
        builder.Property(t => t.BackgroundColor).HasMaxLength(7).IsRequired();

        // Stored as text rather than an integer: a theme dump should be readable, and adding a variant
        // must never risk renumbering an existing one.
        builder.Property(t => t.FontChoice).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.ButtonStyle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.LayoutVariant).HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}
