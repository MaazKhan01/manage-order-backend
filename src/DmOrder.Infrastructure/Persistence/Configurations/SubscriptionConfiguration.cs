using DmOrder.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmOrder.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(s => s.Id);

        // Stored as a name, not an ordinal: a subscription row is something a human will read in a
        // support conversation, and reordering the enum must never silently reinterpret history.
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(s => s.Provider).HasMaxLength(40);
        builder.Property(s => s.ProviderCustomerId).HasMaxLength(120);
        builder.Property(s => s.ProviderSubscriptionId).HasMaxLength(120);

        // Exactly one per store in V1. The unique index is what makes that true rather than assumed.
        builder.HasIndex(s => s.StoreId).IsUnique();

        // The sweep that expires trials reads by status and end date.
        builder.HasIndex(s => new { s.Status, s.TrialEndsAt });
    }
}
