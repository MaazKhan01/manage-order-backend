namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// The boundary a real payment provider will sit behind.
///
/// **No provider is configured, and this interface does not pretend otherwise.** There is no
/// implementation that returns a plausible-looking success, because a fake checkout is worse than no
/// checkout: it produces a seller who believes they have paid and a platform that has not been paid.
///
/// The shape is intentionally minimal and provider-agnostic — a redirect to a hosted checkout, and a
/// webhook that tells us what happened. Anything more specific would be guessing at an API we have
/// not chosen.
///
/// Two rules for whoever implements this:
///
///   1. **The webhook is the only thing that may activate a subscription.** A browser returning from
///      a checkout page is not evidence of payment; it is evidence of a redirect.
///   2. **Never store card details.** The provider's opaque customer and subscription ids are the
///      only things that belong in our database.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>
    /// Whether a provider is actually configured and usable.
    ///
    /// The UI reads this to decide whether an upgrade path exists at all. False today.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>The provider's name, for storing against a subscription. Null when unconfigured.</summary>
    string? Name { get; }

    /// <summary>
    /// Begins a checkout and returns where to send the seller.
    ///
    /// Throws <see cref="PaymentProviderNotConfiguredException"/> while no provider exists, rather
    /// than returning a URL that goes nowhere.
    /// </summary>
    Task<string> CreateCheckoutUrlAsync(
        Guid storeId,
        string returnUrl,
        CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when something asks a payment provider to do work and none is configured.
///
/// Distinct from a generic failure so the API can answer honestly — "not open yet" rather than
/// "something went wrong".
/// </summary>
public sealed class PaymentProviderNotConfiguredException()
    : InvalidOperationException("No payment provider is configured.");
