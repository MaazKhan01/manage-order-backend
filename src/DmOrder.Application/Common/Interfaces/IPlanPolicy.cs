namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// The commercial rules, as configuration rather than constants.
///
/// Pricing is not set, and neither is the free product limit. Both live behind this so the day they
/// are decided is a configuration change, not a code change — and so nothing in the codebase has to
/// invent a number in the meantime.
/// </summary>
public interface IPlanPolicy
{
    /// <summary>How long a new store gets order management for free.</summary>
    TimeSpan TrialLength { get; }

    /// <summary>
    /// How many products a store may publish without a paid subscription.
    ///
    /// Null means "no limit configured", and no limit is then enforced. That is deliberate: the
    /// number has not been decided, and guessing one would start refusing real sellers' products.
    /// </summary>
    int? FreeProductLimit { get; }
}
