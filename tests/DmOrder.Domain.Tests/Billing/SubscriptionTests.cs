using DmOrder.Domain.Billing;
using DmOrder.Domain.Exceptions;
using Shouldly;

namespace DmOrder.Domain.Tests.Billing;

public class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneMonth = TimeSpan.FromDays(30);

    private static Subscription Trial() =>
        Subscription.StartTrial(Guid.CreateVersion7(), Now, OneMonth);

    [Fact]
    public void ANewStoreStartsOnATrial()
    {
        var subscription = Trial();

        subscription.Status.ShouldBe(SubscriptionStatus.Trialing);
        subscription.TrialStartedAt.ShouldBe(Now);
        subscription.TrialEndsAt.ShouldBe(Now + OneMonth);
        subscription.IsManagementUnlocked(Now).ShouldBeTrue();
    }

    [Fact]
    public void ManagementLocksTheMomentTheTrialRunsOut()
    {
        var subscription = Trial();

        subscription.IsManagementUnlocked(Now + OneMonth - TimeSpan.FromSeconds(1)).ShouldBeTrue();
        subscription.IsManagementUnlocked(Now + OneMonth).ShouldBeFalse();
    }

    [Fact]
    public void AnExpiredTrialReadsAsExpiredBeforeAnythingWritesToTheRow()
    {
        var subscription = Trial();

        // Stored status is still Trialing - nothing has run. The effective status must not repeat
        // that lie to someone who is already locked out.
        subscription.Status.ShouldBe(SubscriptionStatus.Trialing);
        subscription.EffectiveStatus(Now + OneMonth).ShouldBe(SubscriptionStatus.TrialExpired);
    }

    [Fact]
    public void MarkingATrialExpiredIsIdempotentAndDoesNotRunEarly()
    {
        var subscription = Trial();

        subscription.MarkTrialExpired(Now + TimeSpan.FromDays(1));
        subscription.Status.ShouldBe(SubscriptionStatus.Trialing);

        subscription.MarkTrialExpired(Now + OneMonth);
        subscription.Status.ShouldBe(SubscriptionStatus.TrialExpired);

        // A sweep can run as often as it likes.
        subscription.MarkTrialExpired(Now + OneMonth + TimeSpan.FromDays(5));
        subscription.Status.ShouldBe(SubscriptionStatus.TrialExpired);
    }

    [Fact]
    public void ActivatingFromAProviderUnlocksManagement()
    {
        var subscription = Trial();
        subscription.MarkTrialExpired(Now + OneMonth);

        var later = Now + OneMonth;
        subscription.ActivateFromProvider("example", "cus_1", "sub_1", later, later + OneMonth);

        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.IsManagementUnlocked(later).ShouldBeTrue();
        // Well past the trial, because the trial is no longer what is unlocking it.
        subscription.IsManagementUnlocked(later + TimeSpan.FromDays(10)).ShouldBeTrue();
    }

    [Fact]
    public void APastDueOrCancelledSubscriptionLocksManagement()
    {
        var pastDue = Trial();
        pastDue.MarkPastDue();
        pastDue.IsManagementUnlocked(Now).ShouldBeFalse();

        var cancelled = Trial();
        cancelled.Cancel(Now);
        cancelled.IsManagementUnlocked(Now).ShouldBeFalse();
        cancelled.CancelledAt.ShouldBe(Now);
    }

    [Fact]
    public void CancellingKeepsTheProviderIdsSoAReturningSellerIsRecognised()
    {
        var subscription = Trial();
        subscription.ActivateFromProvider("example", "cus_1", "sub_1", Now, Now + OneMonth);

        subscription.Cancel(Now + TimeSpan.FromDays(5));

        subscription.ProviderCustomerId.ShouldBe("cus_1");
        subscription.ProviderSubscriptionId.ShouldBe("sub_1");
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(29, 1)]
    [InlineData(30, null)]
    public void ReportsTrialDaysRemaining(int daysElapsed, int? expected)
    {
        var subscription = Trial();
        var at = Now + TimeSpan.FromDays(daysElapsed);

        if (expected is null)
        {
            // At expiry the trial is over; zero rather than a negative number.
            subscription.TrialDaysRemaining(at).ShouldBe(0);
        }
        else
        {
            subscription.TrialDaysRemaining(at).ShouldBe(expected);
        }
    }

    [Fact]
    public void RefusesAnImpossibleTrialOrBillingPeriod()
    {
        Should.Throw<BusinessRuleException>(
            () => Subscription.StartTrial(Guid.CreateVersion7(), Now, TimeSpan.Zero));

        var subscription = Trial();

        Should.Throw<BusinessRuleException>(
            () => subscription.ActivateFromProvider("example", "c", "s", Now, Now));

        Should.Throw<BusinessRuleException>(
            () => subscription.ActivateFromProvider("", "c", "s", Now, Now + OneMonth));
    }
}
