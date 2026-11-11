using System.Collections.Concurrent;
using GymHub.Modules.Billing.Payments;
using GymHub.Modules.Programs.Contracts;

namespace GH.FunctionalTests;

/// <summary>Records Stripe calls and hands out fake ids instead of calling Stripe.</summary>
internal sealed class FakeStripeGateway : IStripeGateway
{
    public ConcurrentQueue<CheckoutRequest> Checkouts { get; } = new();
    public ConcurrentQueue<(string SubscriptionId, string PriceId)> ScheduledChanges { get; } = new();
    public ConcurrentQueue<string> CancelledAtPeriodEnd { get; } = new();
    public ConcurrentQueue<string> CancelledNow { get; } = new();
    public ConcurrentQueue<string> Refunds { get; } = new();
    public ConcurrentQueue<string> ExpiredCheckouts { get; } = new();
    public ConcurrentDictionary<string, StripeAccountStatus> Accounts { get; } = new();

    /// <summary>The payment intent each invoice was paid with.</summary>
    public ConcurrentDictionary<string, string> InvoicePayments { get; } = new();

    private static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    public Task<string> CreateExpressAccountAsync(string trainerId, string? email, CancellationToken cancellationToken)
    {
        var id = NewId("acct");
        Accounts[id] = new StripeAccountStatus(false, false, false);
        return Task.FromResult(id);
    }

    public Task<StripeAccountStatus> GetAccountStatusAsync(string accountId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.GetValueOrDefault(accountId, new StripeAccountStatus(false, false, false)));

    public Task<Uri> CreateOnboardingLinkAsync(string accountId, Uri refreshUrl, Uri returnUrl, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri($"https://connect.stripe.test/setup/{accountId}?return={Uri.EscapeDataString(returnUrl.ToString())}"));

    public Task<Uri> CreateDashboardLinkAsync(string accountId, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri($"https://connect.stripe.test/express/{accountId}"));

    public Task<string> CreateCustomerAsync(string userId, string? email, CancellationToken cancellationToken) => Task.FromResult(NewId("cus"));

    public Task<string> CreateProductAsync(Guid programId, string title, CancellationToken cancellationToken) => Task.FromResult(NewId("prod"));

    public Task<string> CreatePriceAsync(string productId, Guid programId, int priceVersion, ProgramPrice price, CancellationToken cancellationToken) =>
        Task.FromResult(NewId("price"));

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        Checkouts.Enqueue(request);
        var id = NewId("cs");
        return Task.FromResult(new CheckoutSession(id, $"https://checkout.stripe.test/{id}", request.ExpiresAt));
    }

    public Task<bool> ExpireCheckoutAsync(string sessionId, CancellationToken cancellationToken)
    {
        ExpiredCheckouts.Enqueue(sessionId);
        return Task.FromResult(true);
    }

    public Task<Uri> CreatePortalSessionAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri($"https://billing.stripe.test/{customerId}"));

    public Task SchedulePriceChangeAsync(string subscriptionId, string priceId, BillingInterval interval, CancellationToken cancellationToken)
    {
        ScheduledChanges.Enqueue((subscriptionId, priceId));
        return Task.CompletedTask;
    }

    public Task CancelAtPeriodEndAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        CancelledAtPeriodEnd.Enqueue(subscriptionId);
        return Task.CompletedTask;
    }

    public Task CancelNowAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        CancelledNow.Enqueue(subscriptionId);
        return Task.CompletedTask;
    }

    public Task RefundAsync(string paymentIntentId, Guid purchaseId, CancellationToken cancellationToken)
    {
        Refunds.Enqueue(paymentIntentId);
        return Task.CompletedTask;
    }

    public Task<string?> GetInvoicePaymentIntentAsync(string invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(InvoicePayments.TryGetValue(invoiceId, out var id) ? id : null);
}
