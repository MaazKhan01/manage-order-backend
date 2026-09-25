using DmOrder.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace DmOrder.Infrastructure.Billing;

/// <summary>
/// Commercial settings, from configuration.
///
/// The free product limit is deliberately unset by default. It has not been decided, and a guessed
/// number would start refusing real sellers' products — so unset means unenforced.
/// </summary>
public sealed class PlanOptions
{
    public const string SectionName = "Plans";

    /// <summary>Days of free order management for a new store. One month.</summary>
    public int TrialDays { get; set; } = 30;

    /// <summary>
    /// Products a store may publish without a paid subscription. Null or non-positive means no limit
    /// is enforced.
    /// </summary>
    public int? FreeProductLimit { get; set; }
}

public sealed class PlanPolicy(IOptions<PlanOptions> options) : IPlanPolicy
{
    public TimeSpan TrialLength =>
        TimeSpan.FromDays(options.Value.TrialDays > 0 ? options.Value.TrialDays : 30);

    public int? FreeProductLimit =>
        options.Value.FreeProductLimit is { } limit && limit > 0 ? limit : null;
}

/// <summary>
/// The payment provider that exists today: none.
///
/// This is not a stub that pretends to work. Every call that would take money throws, and
/// <see cref="IsConfigured"/> is false so the UI never offers a button that cannot do anything.
///
/// Replacing it means adding a real implementation and selecting it here by configuration — the same
/// shape as <c>IFileStorage</c>.
/// </summary>
public sealed class UnconfiguredPaymentProvider : IPaymentProvider
{
    public bool IsConfigured => false;

    public string? Name => null;

    public Task<string> CreateCheckoutUrlAsync(
        Guid storeId,
        string returnUrl,
        CancellationToken cancellationToken) =>
        throw new PaymentProviderNotConfiguredException();
}
