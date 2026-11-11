using EventBus.Events;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Programs.Contracts;

namespace GymHub.Modules.Billing.Domain;

public enum PurchaseKind
{
    OneTime,
    Subscription
}

public enum PurchaseStatus
{
    /// <summary>Checkout started but not completed.</summary>
    Pending,
    Trialing,
    Active,

    /// <summary>A renewal failed and Stripe is retrying. Access continues.</summary>
    PastDue,
    Ended,
    Refunded,
    Disputed
}

/// <summary>
/// One checkout of one program by one member: a one-time payment or a Stripe subscription. Raises the events that
/// grant and end the buyer's access in the Subscriptions module.
/// </summary>
public sealed class Purchase
{
    private readonly List<IntegrationEvent> _events = [];

    private Purchase() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = "";
    public Guid ProgramId { get; private set; }
    public string TrainerId { get; private set; } = "";
    public PurchaseKind Kind { get; private set; }
    public PurchaseStatus Status { get; private set; }

    /// <summary>The program price version this purchase is billed at (changes when a scheduled price change applies).</summary>
    public int PriceVersion { get; private set; }

    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = "";
    public BillingInterval? Interval { get; private set; }
    public decimal FeePercent { get; private set; }
    public int TrialDays { get; private set; }

    public string? CheckoutSessionId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTime? CheckoutExpiresAt { get; private set; }

    /// <summary>The latest payment: the one-time payment, or the latest paid subscription invoice.</summary>
    public string? PaymentIntentId { get; private set; }

    public string? SubscriptionId { get; private set; }

    /// <summary>Last subscription status seen from Stripe, kept while the purchase is still pending.</summary>
    public string? StripeStatus { get; private set; }

    public DateTime? CurrentPeriodEnd { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTime? TrialEndsAt { get; private set; }

    /// <summary>A price change waiting for the next renewal.</summary>
    public int? ScheduledPriceVersion { get; private set; }

    public long? ScheduledAmountMinor { get; private set; }
    public BillingInterval? ScheduledInterval { get; private set; }

    /// <summary>The program became free; the buyer already has free access and this subscription just runs out.</summary>
    public bool ConvertedToFree { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? LastPaymentAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public string? RefundedBy { get; private set; }

    /// <summary>
    /// Postgres xmin, as an optimistic concurrency token: webhooks, refunds and price-change handlers update the same
    /// purchase, and a stale write fails instead of overwriting a newer status.
    /// </summary>
    public uint Version { get; private set; }

    public bool GivesAccess => Status is PurchaseStatus.Trialing or PurchaseStatus.Active or PurchaseStatus.PastDue;

    public bool IsFinished => Status is PurchaseStatus.Ended or PurchaseStatus.Refunded or PurchaseStatus.Disputed;

    internal IReadOnlyList<IntegrationEvent> Events => _events;

    internal void ClearEvents() => _events.Clear();

    public static Result<Purchase> Start(
        string userId, ProgramSummary program, decimal feePercent, bool trialEligible, DateTime now)
    {
        var price = program.Price;

        if (price.IsFree || price.AmountMinor is not > 0 || string.IsNullOrEmpty(price.Currency))
        {
            return Error.Conflict("This program is free. Join it without checkout.");
        }

        if (program.TrainerId == userId)
        {
            return Error.Conflict("You can't buy your own program.");
        }

        var recurring = price.Model == PricingModel.Recurring;

        return new Purchase
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ProgramId = program.Id,
            TrainerId = program.TrainerId,
            Kind = recurring ? PurchaseKind.Subscription : PurchaseKind.OneTime,
            Status = PurchaseStatus.Pending,
            PriceVersion = program.PriceVersion,
            AmountMinor = price.AmountMinor.Value,
            Currency = price.Currency,
            Interval = recurring ? price.Interval : null,
            FeePercent = feePercent,
            TrialDays = recurring && trialEligible ? price.TrialDays ?? 0 : 0,
            CreatedAt = now
        };
    }

    /// <summary>The platform fee on one payment, in minor units.</summary>
    public static long ApplicationFee(long amountMinor, decimal feePercent) =>
        (long)Math.Round(amountMinor * feePercent / 100m, MidpointRounding.AwayFromZero);

    public long FeeMinor => ApplicationFee(AmountMinor, FeePercent);

    public bool HasOpenCheckout(DateTime now) =>
        Status == PurchaseStatus.Pending && CheckoutUrl is not null && CheckoutExpiresAt > now.AddMinutes(1);

    /// <summary>Stripe could still take payment for this checkout (unlike <see cref="HasOpenCheckout"/>, no margin).</summary>
    public bool CheckoutMayBePaid(DateTime now) =>
        Status == PurchaseStatus.Pending && CheckoutUrl is not null && CheckoutExpiresAt > now;

    /// <summary>The checkout session was expired at Stripe, so it can't be reused or paid.</summary>
    public void CloseCheckout()
    {
        if (Status != PurchaseStatus.Pending)
        {
            return;
        }

        CheckoutUrl = null;
        CheckoutExpiresAt = null;
    }

    public void AttachCheckout(string sessionId, string url, DateTime expiresAt)
    {
        CheckoutSessionId = sessionId;
        CheckoutUrl = url;
        CheckoutExpiresAt = expiresAt;
    }

    /// <summary>Checkout finished for a one-time purchase.</summary>
    public void CompletePayment(string? paymentIntentId, DateTime now)
    {
        if (Status != PurchaseStatus.Pending || Kind != PurchaseKind.OneTime)
        {
            return;
        }

        PaymentIntentId = paymentIntentId;
        LastPaymentAt = now;
        Status = PurchaseStatus.Active;
        _events.Add(new PaidAccessGranted(UserId, ProgramId, Id));
    }

    /// <summary>Checkout finished for a subscription.</summary>
    public void CompleteSubscription(string subscriptionId)
    {
        if (Status != PurchaseStatus.Pending || Kind != PurchaseKind.Subscription)
        {
            return;
        }

        SubscriptionId = subscriptionId;
        Status = MapStatus(StripeStatus) ?? (TrialDays > 0 ? PurchaseStatus.Trialing : PurchaseStatus.Active);

        if (IsFinished)
        {
            EndedAt = CreatedAt;
            return;
        }

        _events.Add(new PaidAccessGranted(UserId, ProgramId, Id));
    }

    /// <summary>
    /// Applies the subscription's state from Stripe. <paramref name="priceVersion"/> is the program price version of
    /// the subscription's current Stripe price, when known.
    /// </summary>
    public void SyncSubscription(
        string subscriptionId, string status, bool cancelAtPeriodEnd, DateTime? periodEnd, DateTime? trialEnd, int? priceVersion, DateTime now)
    {
        if (Kind != PurchaseKind.Subscription || IsFinished)
        {
            return;
        }

        SubscriptionId = subscriptionId;
        StripeStatus = status;
        CancelAtPeriodEnd = cancelAtPeriodEnd;
        CurrentPeriodEnd = periodEnd ?? CurrentPeriodEnd;
        TrialEndsAt = trialEnd;

        if (priceVersion is { } version && version != PriceVersion && ScheduledPriceVersion == version)
        {
            PriceVersion = version;
            AmountMinor = ScheduledAmountMinor ?? AmountMinor;
            Interval = ScheduledInterval ?? Interval;
            ScheduledPriceVersion = null;
            ScheduledAmountMinor = null;
            ScheduledInterval = null;
        }

        if (Status == PurchaseStatus.Pending)
        {
            // Access starts with checkout.session.completed; until then only remember what Stripe said.
            return;
        }

        switch (MapStatus(status))
        {
            case PurchaseStatus.Ended:
                End(status == "canceled" ? PaidAccessEndReason.Cancelled : PaidAccessEndReason.Unpaid, now);
                break;
            case { } mapped:
                Status = mapped;
                break;
        }
    }

    /// <summary>A subscription invoice was paid.</summary>
    public void RecordPayment(string? paymentIntentId, DateTime now)
    {
        if (paymentIntentId is not null)
        {
            PaymentIntentId = paymentIntentId;
        }

        LastPaymentAt = now;
    }

    public void SchedulePriceChange(int version, long amountMinor, BillingInterval interval)
    {
        ScheduledPriceVersion = version;
        ScheduledAmountMinor = amountMinor;
        ScheduledInterval = interval;
    }

    /// <summary>Renewals stop; access lasts until the period ends.</summary>
    public void StopRenewing() => CancelAtPeriodEnd = true;

    /// <summary>The program became free: the buyer keeps access as a free member and the subscription runs out.</summary>
    public void ConvertToFree()
    {
        if (ConvertedToFree || !GivesAccess)
        {
            return;
        }

        ConvertedToFree = true;
        CancelAtPeriodEnd = true;
        _events.Add(new PaidAccessConvertedToFree(UserId, ProgramId, Id));
    }

    public Result CanBeRefundedBy(CurrentUser user, int trainerWindowDays, DateTime now)
    {
        if (!user.IsAdmin && user.Id != TrainerId)
        {
            return Error.Forbidden("You can only refund your own sales.");
        }

        if (Status is PurchaseStatus.Pending or PurchaseStatus.Refunded or PurchaseStatus.Disputed)
        {
            return Error.Conflict("This purchase can't be refunded.");
        }

        if (PaymentIntentId is null || LastPaymentAt is null)
        {
            return Error.Conflict("Nothing has been paid for this purchase yet.");
        }

        if (!user.IsAdmin && LastPaymentAt.Value.AddDays(trainerWindowDays) < now)
        {
            return Error.Forbidden($"Sales can be refunded for {trainerWindowDays} days. Ask an admin for older refunds.");
        }

        return Result.Success();
    }

    public void MarkRefunded(string refundedBy, DateTime now)
    {
        if (Status is PurchaseStatus.Refunded or PurchaseStatus.Disputed or PurchaseStatus.Pending)
        {
            return;
        }

        var hadAccess = GivesAccess;
        Status = PurchaseStatus.Refunded;
        RefundedBy = refundedBy;
        EndedAt ??= now;

        if (hadAccess)
        {
            _events.Add(new PaidAccessEnded(UserId, ProgramId, Id, PaidAccessEndReason.Refunded));
        }
    }

    public void MarkDisputed(DateTime now)
    {
        if (Status is PurchaseStatus.Disputed or PurchaseStatus.Pending)
        {
            return;
        }

        var hadAccess = GivesAccess;
        Status = PurchaseStatus.Disputed;
        EndedAt ??= now;

        if (hadAccess)
        {
            _events.Add(new PaidAccessEnded(UserId, ProgramId, Id, PaidAccessEndReason.Disputed));
        }
    }

    private void End(PaidAccessEndReason reason, DateTime now)
    {
        Status = PurchaseStatus.Ended;
        EndedAt = now;
        _events.Add(new PaidAccessEnded(UserId, ProgramId, Id, reason));
    }

    /// <summary>Maps a Stripe subscription status. Null for statuses that don't change ours (e.g. incomplete).</summary>
    private static PurchaseStatus? MapStatus(string? stripeStatus) => stripeStatus switch
    {
        "trialing" => PurchaseStatus.Trialing,
        "active" => PurchaseStatus.Active,
        "past_due" => PurchaseStatus.PastDue,
        "canceled" or "unpaid" or "incomplete_expired" => PurchaseStatus.Ended,
        _ => null
    };
}
