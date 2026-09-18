namespace DmOrder.Domain.Common;

/// <summary>
/// Marks an entity as belonging to exactly one store (tenant).
/// The persistence layer uses this marker to stamp and to verify <see cref="StoreId"/> on every write,
/// which is the last line of defence behind explicit store-scoped queries in the Application layer.
/// </summary>
public interface ITenantOwned
{
    Guid StoreId { get; }
}
