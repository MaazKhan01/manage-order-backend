using DmOrder.Domain.Common;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Billing;

/// <summary>
/// Where a store stands with us commercially.
///
/// The product has two halves and only one of them is paid for:
///
///   • The **storefront** is free forever. Nothing in this type may ever take it offline — a seller
///     who stops paying keeps their page, their products and their link. Customers keep ordering.
///   • **Order management** — the dashboard where orders are read, moved and annotated — is free for
///     a month and paid for after that.
///
/// So this type answers exactly one question: <see cref="IsManagementUnlocked"/>. Everything else
/// here exists to explain that answer to a person.
///
/// Nothing is ever deleted when a subscription lapses. Orders, customers and products stay exactly
/// where they are, and restoring access is a state change rather than a recovery.
/// </summary>
public sealed class Subscription : Entity, ITenantOwned
{
    private Subscription() { }

    public Guid StoreId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset TrialStartedAt { get; private set; }

    /// <summary>
    /// The instant the trial stops unlocking management. Kept even after the trial ends, so "your
    /// trial ended on the 3rd" is answerable rather than inferred.
    /// </summary>
    public DateTimeOffset TrialEndsAt { get; private set; }

    /// <summary>
    /// Which payment provider this subscription lives in, once one exists. Null means the store has
    /// never been through a provider — which is every store today, because none is configured.
    /// </summary>
    public string? Provider { get; private set; }

    /// <summary>
    /// The provider's own identifiers. Opaque strings we hand back to them; we never store a card,
    /// a token, or anything that could be used to charge someone.
    /// </summary>
    public string? ProviderCustomerId { get; private set; }

    public string? ProviderSubscriptionId { get; private set; }

    /// <summary>The paid period the provider says we are in. Null until a subscription starts.</summary>
    public DateTimeOffset? CurrentPeriodStart { get; private set; }

    public DateTimeOffset? CurrentPeriodEnd { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>
    /// Starts a store on its trial. Called once, when the store is created — the trial measures from
    /// the moment there is something to manage, not from registration.
    /// </summary>
    public static Subscription StartTrial(Guid storeId, DateTimeOffset now, TimeSpan trialLength)
    {
        if (trialLength <= TimeSpan.Zero)
        {
            throw new BusinessRuleException("A trial must last longer than zero.");
        }

        return new Subscription
        {
            StoreId = storeId,
            Status = SubscriptionStatus.Trialing,
            TrialStartedAt = now,
            TrialEndsAt = now + trialLength,
        };
    }

    /// <summary>
    /// Whether the seller can use order management right now.
    ///
    /// This is the only question the rest of the system asks, and the only place the rule lives.
    /// <see cref="Status"/> is a stored summary that a background job or a webhook updates; the trial
    /// is checked against the clock as well, so an expired trial locks the moment it expires rather
    /// than whenever something next happens to run.
    /// </summary>
    public bool IsManagementUnlocked(DateTimeOffset now) =>
        Status switch
        {
            SubscriptionStatus.Trialing => now < TrialEndsAt,
            SubscriptionStatus.Active => true,

            // A failed payment locks rather than grace-periods, because there is no payment provider
            // to recover from one yet. When billing exists, a grace period is a business decision to
            // make deliberately — and this is the line to change.
            SubscriptionStatus.PastDue => false,

            _ => false,
        };

    /// <summary>
    /// The status as it should be read *now*, folding in a trial that has quietly run out.
    ///
    /// Stored status alone lies between the instant a trial expires and whatever next writes to the
    /// row, and the UI must never say "trialing" to someone who is locked out.
    /// </summary>
    public SubscriptionStatus EffectiveStatus(DateTimeOffset now) =>
        Status == SubscriptionStatus.Trialing && now >= TrialEndsAt
            ? SubscriptionStatus.TrialExpired
            : Status;

    /// <summary>Days left in the trial, floored at zero. Null once the trial is no longer the thing.</summary>
    public int? TrialDaysRemaining(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trialing) return null;

        var remaining = TrialEndsAt - now;
        return remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalDays);
    }

    /// <summary>
    /// Records that a trial has run out. Idempotent, so a scheduled sweep can run as often as it
    /// likes without churning rows.
    /// </summary>
    public void MarkTrialExpired(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trialing || now < TrialEndsAt)
        {
            return;
        }

        Status = SubscriptionStatus.TrialExpired;
    }

    /// <summary>
    /// Applied when a provider confirms a paid subscription.
    ///
    /// Only ever called from a verified provider webhook or an equivalent server-to-server read.
    /// Nothing a browser sends may reach this — "the checkout page redirected back" is not evidence
    /// that anybody paid.
    /// </summary>
    public void ActivateFromProvider(
        string provider,
        string providerCustomerId,
        string providerSubscriptionId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new BusinessRuleException("A payment provider is required.");
        }

        if (periodEnd <= periodStart)
        {
            throw new BusinessRuleException("A billing period must end after it starts.");
        }

        Provider = provider.Trim();
        ProviderCustomerId = providerCustomerId;
        ProviderSubscriptionId = providerSubscriptionId;
        CurrentPeriodStart = periodStart;
        CurrentPeriodEnd = periodEnd;
        Status = SubscriptionStatus.Active;
        CancelledAt = null;
    }

    /// <summary>A payment failed. The seller keeps their data and their storefront; management locks.</summary>
    public void MarkPastDue()
    {
        Status = SubscriptionStatus.PastDue;
    }

    /// <summary>
    /// Cancelled, by the seller or the provider. Deliberately does not clear the provider ids: they
    /// are how the same person is recognised if they come back.
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        Status = SubscriptionStatus.Cancelled;
        CancelledAt = now;
    }
}
