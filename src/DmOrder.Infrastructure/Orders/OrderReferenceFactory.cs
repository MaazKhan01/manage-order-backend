using DmOrder.Application.Common.Interfaces;
using DmOrder.Domain.Orders;
using Microsoft.Extensions.Options;

namespace DmOrder.Infrastructure.Orders;

/// <summary>
/// Settings for the customer-facing order reference.
///
/// The prefix is configuration because the platform is renameable. Changing it does not rewrite
/// existing references — an order a customer already has written down must keep working — so old
/// and new prefixes simply coexist.
/// </summary>
public sealed class OrderReferenceOptions
{
    public const string SectionName = "Orders";

    public string ReferencePrefix { get; set; } = OrderReference.DefaultPrefix;
}

public sealed class OrderReferenceFactory(
    IOptions<OrderReferenceOptions> options,
    IDateTimeProvider clock) : IOrderReferenceFactory
{
    public string Next() => OrderReference.Generate(options.Value.ReferencePrefix, clock.UtcNow.Year);
}
