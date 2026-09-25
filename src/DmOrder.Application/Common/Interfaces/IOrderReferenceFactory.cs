namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Produces customer-facing order references.
///
/// An interface rather than a static call because the prefix is configuration and the year comes
/// from the clock — both of which belong in Infrastructure, and both of which a test needs to pin.
/// The format itself lives in <c>DmOrder.Domain.Orders.OrderReference</c>.
/// </summary>
public interface IOrderReferenceFactory
{
    /// <summary>
    /// A fresh reference. Random, so two calls in the same millisecond differ — the caller still
    /// retries on a unique violation, because "astronomically unlikely" is not "impossible".
    /// </summary>
    string Next();
}
