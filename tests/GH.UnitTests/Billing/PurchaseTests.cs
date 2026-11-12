using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Programs.Contracts;

namespace GH.UnitTests.Billing;

public class PurchaseTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CurrentUser Trainer = new("trainer", IsTrainer: true, IsAdmin: false);
    private static readonly CurrentUser OtherTrainer = new("other", IsTrainer: true, IsAdmin: false);
    private static readonly CurrentUser Admin = new("admin", IsTrainer: false, IsAdmin: true);

    private static readonly ProgramPrice Monthly = new(PricingModel.Recurring, 1999, "USD", BillingInterval.Monthly, 7);
    private static readonly ProgramPrice OneTime = new(PricingModel.OneTime, 4900, "USD", null);

    private static ProgramSummary Program(ProgramPrice price) =>
        new(Guid.CreateVersion7(), "trainer", "Squat Lab", ProgramStatus.Published, price, 3);

    private static Purchase Start(ProgramPrice price, bool trialEligible = true) =>
        Purchase.Start("member", Program(price), 10m, trialEligible, Now).Value!;

    [Theory]
    [InlineData(4900, 10, 490)]
    [InlineData(1999, 10, 200)]
    [InlineData(1999, 12.5, 250)]
    public void The_platform_fee_is_a_rounded_percent(long amount, double percent, long fee)
    {
        Assert.Equal(fee, Purchase.ApplicationFee(amount, (decimal)percent));
    }

    [Fact]
    public void Free_programs_and_a_trainers_own_programs_are_not_sold()
    {
        Assert.Equal(ErrorKind.Conflict, Purchase.Start("member", Program(ProgramPrice.Free), 10m, true, Now).Error?.Kind);
        Assert.Equal(ErrorKind.Conflict, Purchase.Start("trainer", Program(OneTime), 10m, true, Now).Error?.Kind);
    }

    [Fact]
    public void A_purchase_records_the_price_it_was_sold_at()
    {
        var purchase = Start(Monthly);

        Assert.Equal(PurchaseKind.Subscription, purchase.Kind);
        Assert.Equal(3, purchase.PriceVersion);
        Assert.Equal(1999, purchase.AmountMinor);
        Assert.Equal(7, purchase.TrialDays);
        Assert.Equal(0, Start(Monthly, trialEligible: false).TrialDays);
        Assert.Equal(0, Start(OneTime).TrialDays);
    }

    [Fact]
    public void Completing_a_payment_grants_access_once()
    {
        var purchase = Start(OneTime);

        purchase.CompletePayment("pi_1", Now);
        purchase.CompletePayment("pi_1", Now);

        Assert.Equal(PurchaseStatus.Active, purchase.Status);
        Assert.Single(purchase.Events.OfType<PaidAccessGranted>());
        Assert.False(Assert.Single(purchase.Events.OfType<ProgramSold>()).IsSubscription);
    }

    [Fact]
    public void A_subscription_keeps_the_status_stripe_reported_before_checkout_completed()
    {
        var purchase = Start(Monthly);

        purchase.SyncSubscription("sub_1", "trialing", false, Now.AddDays(7), Now.AddDays(7), null, Now);
        Assert.Empty(purchase.Events);

        purchase.CompleteSubscription("sub_1");

        Assert.Equal(PurchaseStatus.Trialing, purchase.Status);
        Assert.Single(purchase.Events.OfType<PaidAccessGranted>());
        Assert.True(Assert.Single(purchase.Events.OfType<ProgramSold>()).IsSubscription);
    }

    [Theory]
    [InlineData("canceled", PaidAccessEndReason.Cancelled)]
    [InlineData("unpaid", PaidAccessEndReason.Unpaid)]
    public void A_subscription_that_stops_ends_access(string status, PaidAccessEndReason reason)
    {
        var purchase = Start(Monthly);
        purchase.CompleteSubscription("sub_1");
        purchase.ClearEvents();

        purchase.SyncSubscription("sub_1", "past_due", false, Now, null, null, Now);
        purchase.SyncSubscription("sub_1", "past_due", false, Now, null, null, Now);
        Assert.True(purchase.GivesAccess);
        Assert.Single(purchase.Events.OfType<SubscriptionPaymentFailed>());
        purchase.ClearEvents();

        purchase.SyncSubscription("sub_1", status, false, Now, null, null, Now);

        Assert.Equal(PurchaseStatus.Ended, purchase.Status);
        Assert.Equal(reason, Assert.IsType<PaidAccessEnded>(Assert.Single(purchase.Events)).Reason);
    }

    [Fact]
    public void A_scheduled_price_applies_when_stripe_switches_the_subscription()
    {
        var purchase = Start(Monthly);
        purchase.CompleteSubscription("sub_1");
        purchase.SchedulePriceChange(4, 2499, BillingInterval.Yearly);
        var scheduled = Assert.Single(purchase.Events.OfType<SubscriptionPriceChangeScheduled>());
        Assert.Equal((4, 2499L, "Yearly"), (scheduled.PriceVersion, scheduled.AmountMinor, scheduled.Interval));

        purchase.SyncSubscription("sub_1", "active", false, Now, null, priceVersion: 3, Now);
        Assert.Equal(1999, purchase.AmountMinor);

        purchase.SyncSubscription("sub_1", "active", false, Now, null, priceVersion: 4, Now);

        Assert.Equal(4, purchase.PriceVersion);
        Assert.Equal(2499, purchase.AmountMinor);
        Assert.Equal(BillingInterval.Yearly, purchase.Interval);
        Assert.Null(purchase.ScheduledPriceVersion);
    }

    [Fact]
    public void Trainers_refund_their_own_sales_within_the_window_and_admins_any_time()
    {
        var purchase = Start(OneTime);
        Assert.Equal(ErrorKind.Conflict, purchase.CanBeRefundedBy(Admin, 30, Now).Error?.Kind);

        purchase.CompletePayment("pi_1", Now);

        Assert.True(purchase.CanBeRefundedBy(Trainer, 30, Now.AddDays(29)).IsSuccess);
        Assert.Equal(ErrorKind.Forbidden, purchase.CanBeRefundedBy(Trainer, 30, Now.AddDays(31)).Error?.Kind);
        Assert.Equal(ErrorKind.Forbidden, purchase.CanBeRefundedBy(OtherTrainer, 30, Now).Error?.Kind);
        Assert.True(purchase.CanBeRefundedBy(Admin, 30, Now.AddDays(365)).IsSuccess);
    }

    [Fact]
    public void Refunds_and_disputes_end_access_once()
    {
        var purchase = Start(OneTime);
        purchase.CompletePayment("pi_1", Now);
        purchase.ClearEvents();

        purchase.MarkRefunded("trainer", Now);
        purchase.MarkRefunded("stripe", Now);
        purchase.MarkDisputed(Now);

        Assert.Equal(PurchaseStatus.Disputed, purchase.Status);
        Assert.Equal(PaidAccessEndReason.Refunded, Assert.IsType<PaidAccessEnded>(Assert.Single(purchase.Events)).Reason);
    }

    [Fact]
    public void Converting_to_free_keeps_access_and_stops_renewals()
    {
        var purchase = Start(Monthly);
        purchase.CompleteSubscription("sub_1");
        purchase.ClearEvents();

        purchase.ConvertToFree();
        purchase.ConvertToFree();

        Assert.True(purchase.CancelAtPeriodEnd);
        Assert.IsType<PaidAccessConvertedToFree>(Assert.Single(purchase.Events));
    }
}
