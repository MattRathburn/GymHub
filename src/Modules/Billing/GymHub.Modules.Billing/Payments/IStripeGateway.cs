using GymHub.Modules.Programs.Contracts;

namespace GymHub.Modules.Billing.Payments;

public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Stripe secret key (sk_test_… locally). From the AppHost's stripe-secret-key parameter.</summary>
    public string? StripeSecretKey { get; set; }

    /// <summary>Signing secret of the platform webhook endpoint (or of `stripe listen`).</summary>
    public string? StripeWebhookSecret { get; set; }

    /// <summary>Signing secret of the Connect webhook endpoint, when it is a separate endpoint.</summary>
    public string? StripeConnectWebhookSecret { get; set; }

    /// <summary>GymHub's share of every payment, in percent.</summary>
    public decimal PlatformFeePercent { get; set; } = 10;

    /// <summary>How long after a payment its trainer can refund it. Admins can refund any time.</summary>
    public int TrainerRefundWindowDays { get; set; } = 30;

    /// <summary>The browser-facing origin (the Gateway), for Stripe's return links.</summary>
    public Uri PublicBaseUrl { get; set; } = new("https://localhost:7179");

    public IEnumerable<string> WebhookSecrets =>
        new[] { StripeWebhookSecret, StripeConnectWebhookSecret }.Where(s => !string.IsNullOrEmpty(s))!;
}

public sealed record StripeAccountStatus(bool ChargesEnabled, bool PayoutsEnabled, bool DetailsSubmitted);

public sealed record CheckoutRequest(
    Guid PurchaseId,
    string CustomerId,
    string PriceId,
    bool Recurring,
    long ApplicationFeeMinor,
    decimal ApplicationFeePercent,
    int TrialDays,
    string DestinationAccountId,
    Uri SuccessUrl,
    Uri CancelUrl,
    DateTime ExpiresAt);

public sealed record CheckoutSession(string Id, string Url, DateTime ExpiresAt);

/// <summary>
/// Every Stripe call Billing makes. The real one is <see cref="StripeGateway"/>; functional tests use a fake.
/// Calls that create something take an idempotency key built from GymHub ids.
/// </summary>
public interface IStripeGateway
{
    Task<string> CreateExpressAccountAsync(string trainerId, string? email, CancellationToken cancellationToken);

    Task<StripeAccountStatus> GetAccountStatusAsync(string accountId, CancellationToken cancellationToken);

    Task<Uri> CreateOnboardingLinkAsync(string accountId, Uri refreshUrl, Uri returnUrl, CancellationToken cancellationToken);

    Task<Uri> CreateDashboardLinkAsync(string accountId, CancellationToken cancellationToken);

    Task<string> CreateCustomerAsync(string userId, string? email, CancellationToken cancellationToken);

    Task<string> CreateProductAsync(Guid programId, string title, CancellationToken cancellationToken);

    Task<string> CreatePriceAsync(string productId, Guid programId, int priceVersion, ProgramPrice price, CancellationToken cancellationToken);

    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>Expires a checkout session if it is still open. False when it was already completed (paid).</summary>
    Task<bool> ExpireCheckoutAsync(string sessionId, CancellationToken cancellationToken);

    Task<Uri> CreatePortalSessionAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken);

    /// <summary>Moves a subscription to another price at its next renewal, without proration.</summary>
    Task SchedulePriceChangeAsync(string subscriptionId, string priceId, BillingInterval interval, CancellationToken cancellationToken);

    Task CancelAtPeriodEndAsync(string subscriptionId, CancellationToken cancellationToken);

    Task CancelNowAsync(string subscriptionId, CancellationToken cancellationToken);

    /// <summary>Full refund, taking back the trainer's transfer and GymHub's fee.</summary>
    Task RefundAsync(string paymentIntentId, Guid purchaseId, CancellationToken cancellationToken);

    /// <summary>The payment intent that paid an invoice, if any.</summary>
    Task<string?> GetInvoicePaymentIntentAsync(string invoiceId, CancellationToken cancellationToken);
}
