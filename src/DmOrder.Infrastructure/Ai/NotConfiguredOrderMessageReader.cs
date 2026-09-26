using DmOrder.Application.Common.Interfaces;

namespace DmOrder.Infrastructure.Ai;

/// <summary>
/// The message reader that exists when no API key is set: none.
///
/// Not a stub that pretends to work. <see cref="IsConfigured"/> is false, so the endpoint answers
/// 503 and the dashboard never offers a button that cannot do anything - the same shape as
/// <c>UnconfiguredPaymentProvider</c>. Deploying without a key is a supported state, not a fault.
/// </summary>
public sealed class NotConfiguredOrderMessageReader : IOrderMessageReader
{
    public bool IsConfigured => false;

    public Task<OrderDraft> ReadAsync(OrderMessageContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "No AI provider is configured. Set Claude:ApiKey to enable reading messages into orders.");
}
