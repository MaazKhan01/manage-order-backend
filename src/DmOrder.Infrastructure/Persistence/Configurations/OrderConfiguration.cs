using DmOrder.Domain.CustomFields;
using DmOrder.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmOrder.Infrastructure.Persistence.Configurations;

public sealed class CustomFieldConfiguration : IEntityTypeConfiguration<CustomField>
{
    public void Configure(EntityTypeBuilder<CustomField> builder)
    {
        builder.ToTable("custom_fields");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Label).HasMaxLength(160).IsRequired();
        builder.Property(f => f.HelpText).HasMaxLength(500);
        builder.Property(f => f.FieldType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(f => f.MinValue).HasColumnType("numeric(12,2)");
        builder.Property(f => f.MaxValue).HasColumnType("numeric(12,2)");

        // The order form loads "fields for this product" plus "fields for every product", so both
        // halves of that query are covered here.
        builder.HasIndex(f => new { f.StoreId, f.ProductId, f.DisplayOrder });

        builder.HasMany(f => f.Options)
            .WithOne()
            .HasForeignKey(o => o.CustomFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(f => f.Options).AutoInclude();
    }
}

public sealed class CustomFieldOptionConfiguration : IEntityTypeConfiguration<CustomFieldOption>
{
    public void Configure(EntityTypeBuilder<CustomFieldOption> builder)
    {
        builder.ToTable("custom_field_options");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Label).HasMaxLength(160).IsRequired();
        builder.Property(o => o.Value).HasMaxLength(160).IsRequired();

        builder.HasIndex(o => new { o.CustomFieldId, o.DisplayOrder });
    }
}

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(160).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.AddressText).HasMaxLength(500);

        // Per store, not global: the same person ordering from two sellers is two records, so neither
        // seller can learn anything about the other's customers.
        builder.HasIndex(c => new { c.StoreId, c.Phone }).IsUnique();
    }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.TotalAmount).HasColumnType("numeric(12,2)");
        builder.Property(o => o.DeliveryAddress).HasMaxLength(500);
        builder.Property(o => o.CustomerNote).HasMaxLength(2000);
        builder.Property(o => o.SubmittedFromIpHash).HasMaxLength(64);

        // The per-store order number is what makes "#1042" unambiguous, and the unique index is what
        // actually guarantees it when two orders arrive at once.
        builder.HasIndex(o => new { o.StoreId, o.OrderNumber }).IsUnique();

        // The seller's order list: their store, filtered by status, newest first.
        builder.HasIndex(o => new { o.StoreId, o.Status, o.CreatedAt });
        builder.HasIndex(o => new { o.StoreId, o.CustomerId });

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            // A customer with orders cannot be deleted out from under them.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items).AutoInclude();
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(i => i.Id);

        // Snapshot, not a lookup: renaming or deleting a product must not rewrite an existing order.
        builder.Property(i => i.ProductNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(i => i.UnitPriceSnapshot).HasColumnType("numeric(12,2)");
        builder.Property(i => i.LineTotal).HasColumnType("numeric(12,2)");

        builder.HasIndex(i => i.OrderId);
        builder.HasIndex(i => i.ProductId);

        builder.HasMany(i => i.FieldValues)
            .WithOne()
            .HasForeignKey(v => v.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.FieldValues).AutoInclude();
    }
}

public sealed class OrderFieldValueConfiguration : IEntityTypeConfiguration<OrderFieldValue>
{
    public void Configure(EntityTypeBuilder<OrderFieldValue> builder)
    {
        builder.ToTable("order_field_values");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.LabelSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(v => v.FieldTypeSnapshot).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(v => v.ValueText).HasMaxLength(4000);
        builder.Property(v => v.ValueNumber).HasColumnType("numeric(18,4)");
        builder.Property(v => v.ValueJson).HasColumnType("jsonb");

        builder.HasIndex(v => new { v.OrderItemId, v.DisplayOrder });

        builder.HasOne<CustomField>()
            .WithMany()
            .HasForeignKey(v => v.CustomFieldId)
            // Deleting a question must not delete the answers customers already gave. The label and
            // type are snapshotted, so the order still renders with the FK gone.
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("order_status_history");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.Note).HasMaxLength(500);

        // Read as a timeline, newest last.
        builder.HasIndex(h => new { h.OrderId, h.CreatedAt });

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrderNoteConfiguration : IEntityTypeConfiguration<OrderNote>
{
    public void Configure(EntityTypeBuilder<OrderNote> builder)
    {
        builder.ToTable("order_notes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Body).HasMaxLength(2000).IsRequired();

        builder.HasIndex(n => new { n.OrderId, n.CreatedAt });

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(n => n.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
