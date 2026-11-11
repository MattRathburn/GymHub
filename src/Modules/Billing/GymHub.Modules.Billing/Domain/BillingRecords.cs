namespace GymHub.Modules.Billing.Domain;

/// <summary>A trainer's Stripe Express account, used as the destination of their sales.</summary>
public sealed class PayoutAccount
{
    private PayoutAccount() { }

    public PayoutAccount(string trainerId, string stripeAccountId, DateTime now)
    {
        TrainerId = trainerId;
        StripeAccountId = stripeAccountId;
        UpdatedAt = now;
    }

    public string TrainerId { get; private set; } = "";
    public string StripeAccountId { get; private set; } = "";
    public bool ChargesEnabled { get; private set; }
    public bool PayoutsEnabled { get; private set; }
    public bool DetailsSubmitted { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsReady => ChargesEnabled;

    public void Update(bool chargesEnabled, bool payoutsEnabled, bool detailsSubmitted, DateTime now)
    {
        ChargesEnabled = chargesEnabled;
        PayoutsEnabled = payoutsEnabled;
        DetailsSubmitted = detailsSubmitted;
        UpdatedAt = now;
    }
}

/// <summary>The Stripe customer that pays for a member's purchases.</summary>
public sealed class BillingCustomer
{
    private BillingCustomer() { }

    public BillingCustomer(string userId, string stripeCustomerId)
    {
        UserId = userId;
        StripeCustomerId = stripeCustomerId;
    }

    public string UserId { get; private set; } = "";
    public string StripeCustomerId { get; private set; } = "";
}

/// <summary>The Stripe product and price for one price version of a program. Stripe prices can't change, so each version gets its own.</summary>
public sealed class CatalogPrice
{
    private CatalogPrice() { }

    public CatalogPrice(Guid programId, int priceVersion, string stripeProductId, string stripePriceId)
    {
        ProgramId = programId;
        PriceVersion = priceVersion;
        StripeProductId = stripeProductId;
        StripePriceId = stripePriceId;
    }

    public Guid ProgramId { get; private set; }
    public int PriceVersion { get; private set; }
    public string StripeProductId { get; private set; } = "";
    public string StripePriceId { get; private set; } = "";
}

/// <summary>
/// One payment of a purchase: the one-time payment or one paid subscription invoice. Lets refunds and disputes of
/// any payment be matched to their purchase, and keeps a record of those that don't end access.
/// </summary>
public sealed class PurchasePayment
{
    private PurchasePayment() { }

    public PurchasePayment(string paymentIntentId, Guid purchaseId, DateTime paidAt)
    {
        PaymentIntentId = paymentIntentId;
        PurchaseId = purchaseId;
        PaidAt = paidAt;
    }

    public string PaymentIntentId { get; private set; } = "";
    public Guid PurchaseId { get; private set; }
    public DateTime PaidAt { get; private set; }

    /// <summary>Total refunded so far, in minor units (Stripe reports the running total).</summary>
    public long RefundedAmountMinor { get; private set; }

    public DateTime? RefundedAt { get; private set; }
    public DateTime? DisputedAt { get; private set; }

    public void RecordRefund(long totalRefundedMinor, DateTime now)
    {
        RefundedAmountMinor = totalRefundedMinor;
        RefundedAt = now;
    }

    public void RecordDispute(DateTime now) => DisputedAt ??= now;
}

/// <summary>A webhook event already handled, so Stripe's retries and duplicates are ignored.</summary>
public sealed class StripeEventRecord
{
    private StripeEventRecord() { }

    public StripeEventRecord(string eventId, string type, DateTime receivedAt)
    {
        EventId = eventId;
        Type = type;
        ReceivedAt = receivedAt;
    }

    public string EventId { get; private set; } = "";
    public string Type { get; private set; } = "";
    public DateTime ReceivedAt { get; private set; }
}
