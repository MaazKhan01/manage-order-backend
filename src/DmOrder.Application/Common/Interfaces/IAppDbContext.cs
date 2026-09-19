using DmOrder.Domain.Catalogue;
using DmOrder.Domain.CustomFields;
using DmOrder.Domain.Media;
using DmOrder.Domain.Orders;
using DmOrder.Domain.Stores;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// The persistence surface available to use cases. DbSets are added here as entities are introduced,
/// so Application can project straight into DTOs without a repository per entity.
/// See docs/ADR/0002-efcore-in-application.md for why this is preferred over generic repositories.
/// </summary>
public interface IAppDbContext
{
    DbSet<Store> Stores { get; }

    DbSet<StoreTheme> StoreThemes { get; }

    DbSet<Category> Categories { get; }

    DbSet<Product> Products { get; }

    DbSet<ProductImage> ProductImages { get; }

    DbSet<CustomField> CustomFields { get; }

    DbSet<CustomFieldOption> CustomFieldOptions { get; }

    DbSet<Customer> Customers { get; }

    DbSet<Order> Orders { get; }

    DbSet<OrderItem> OrderItems { get; }

    DbSet<OrderFieldValue> OrderFieldValues { get; }

    DbSet<OrderStatusHistory> OrderStatusHistory { get; }

    DbSet<OrderNote> OrderNotes { get; }

    DbSet<MediaAsset> MediaAssets { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
