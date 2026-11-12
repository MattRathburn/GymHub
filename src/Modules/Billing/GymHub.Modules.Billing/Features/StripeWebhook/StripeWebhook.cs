using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Payments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace GymHub.Modules.Billing.Features.StripeWebhook;

public enum WebhookOutcome
{
    Handled,
    Duplicate,
    BadSignature
}

/// <summary>
/// Applies Stripe's events to purchases and payout accounts. Each event is handled once (by id) and saved with the
/// outbox events it raises in one SaveChanges. A failure returns 500 so Stripe retries.
/// </summary>
internal sealed class StripeWebhookHandler(
    BillingDbContext db, IStripeGateway stripe, IOptions<BillingOptions> options, TimeProvider timeProvider, ILogger<StripeWebhookHandler> logger)
{
    /// <summary>How old a signature may be. Stripe's default.</summary>
    private const long SignatureTolerance = 300;

    public async Task<WebhookOutcome> Handle(string json, string? signature, CancellationToken cancellationToken)
    {
        if (Verify(json, signature) is not { } stripeEvent)
        {
            return WebhookOutcome.BadSignature;
        }

        if (await db.StripeEvents.AnyAsync(e => e.EventId == stripeEvent.Id, cancellationToken))
        {
            return WebhookOutcome.Duplicate;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        db.StripeEvents.Add(new StripeEventRecord(stripeEvent.Id, stripeEvent.Type, now));

        switch (stripeEvent.Data.Object)
        {
            case Stripe.Checkout.Session session when stripeEvent.Type == EventTypes.CheckoutSessionCompleted:
                await CheckoutCompletedAsync(session, now, cancellationToken);
                break;
            case Subscription subscription when stripeEvent.Type is EventTypes.CustomerSubscriptionCreated
                or EventTypes.CustomerSubscriptionUpdated or EventTypes.CustomerSubscriptionDeleted:
                await SubscriptionChangedAsync(subscription, now, cancellationToken);
                break;
            case Subscription subscription when stripeEvent.Type == EventTypes.CustomerSubscriptionTrialWillEnd:
                await TrialWillEndAsync(subscription, cancellationToken);
                break;
            case Invoice invoice when stripeEvent.Type == EventTypes.InvoicePaid:
                await InvoicePaidAsync(invoice, now, cancellationToken);
                break;
            case Charge charge when stripeEvent.Type == EventTypes.ChargeRefunded:
                await ChargeRefundedAsync(charge, now, cancellationToken);
                break;
            case Dispute dispute when stripeEvent.Type == EventTypes.ChargeDisputeCreated:
                await DisputeCreatedAsync(dispute, now, cancellationToken);
                break;
            case Account account when stripeEvent.Type == EventTypes.AccountUpdated:
                await AccountUpdatedAsync(account, now, cancellationToken);
                break;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!await IsRecordedElsewhereAsync(stripeEvent.Id))
            {
                throw;
            }

            // A concurrent delivery of the same event got there first.
            return WebhookOutcome.Duplicate;
        }

        return WebhookOutcome.Handled;
    }

    private Event? Verify(string json, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return null;
        }

        foreach (var secret in options.Value.WebhookSecrets)
        {
            try
            {
                return EventUtility.ConstructEvent(json, signature, secret, SignatureTolerance, throwOnApiVersionMismatch: false);
            }
            catch (StripeException)
            {
                // Try the next endpoint's secret.
            }
        }

        logger.LogWarning("Rejected a Stripe webhook with an invalid signature");
        return null;
    }

    private async Task CheckoutCompletedAsync(Stripe.Checkout.Session session, DateTime now, CancellationToken cancellationToken)
    {
        if (await FindAsync(session.Metadata, p => p.CheckoutSessionId == session.Id, cancellationToken) is not { } purchase)
        {
            return;
        }

        if (session.Mode == "subscription" && session.SubscriptionId is { } subscriptionId)
        {
            purchase.CompleteSubscription(subscriptionId);
        }
        else if (session.Mode == "payment" && session.PaymentStatus == "paid")
        {
            purchase.CompletePayment(session.PaymentIntentId, now);
            await RecordPaymentAsync(purchase.Id, session.PaymentIntentId, now, cancellationToken);
        }
    }

    private async Task SubscriptionChangedAsync(Subscription subscription, DateTime now, CancellationToken cancellationToken)
    {
        if (await FindAsync(subscription.Metadata, p => p.SubscriptionId == subscription.Id, cancellationToken) is not { } purchase)
        {
            return;
        }

        var item = subscription.Items?.Data.FirstOrDefault();
        int? priceVersion = null;

        if (item?.Price?.Id is { } priceId)
        {
            priceVersion = await db.CatalogPrices.Where(c => c.StripePriceId == priceId).Select(c => (int?)c.PriceVersion).SingleOrDefaultAsync(cancellationToken);
        }

        purchase.SyncSubscription(
            subscription.Id,
            subscription.Status,
            subscription.CancelAtPeriodEnd || subscription.CancelAt is not null,
            item?.CurrentPeriodEnd,
            subscription.TrialEnd,
            priceVersion,
            now);
    }

    private async Task TrialWillEndAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var purchase = await FindAsync(subscription.Metadata, p => p.SubscriptionId == subscription.Id, cancellationToken);
        purchase?.TrialEnding(subscription.TrialEnd);
    }

    private async Task InvoicePaidAsync(Invoice invoice, DateTime now, CancellationToken cancellationToken)
    {
        var details = invoice.Parent?.SubscriptionDetails;

        if (details?.SubscriptionId is not { } subscriptionId || invoice.AmountPaid <= 0)
        {
            return;
        }

        // The first invoice can be paid before the events that store the subscription id; its metadata names the purchase.
        if (await FindAsync(details.Metadata, p => p.SubscriptionId == subscriptionId, cancellationToken) is not { } purchase)
        {
            return;
        }

        var paymentIntentId = await stripe.GetInvoicePaymentIntentAsync(invoice.Id, cancellationToken);
        purchase.RecordPayment(paymentIntentId, now);
        await RecordPaymentAsync(purchase.Id, paymentIntentId, now, cancellationToken);
    }

    /// <summary>
    /// A full refund of the purchase's latest payment made outside GymHub (e.g. the Stripe dashboard) ends access; ours
    /// are already marked. A refund of an older payment, or a partial refund, is only recorded on that payment.
    /// </summary>
    private async Task ChargeRefundedAsync(Charge charge, DateTime now, CancellationToken cancellationToken)
    {
        if (charge.PaymentIntentId is null || charge.AmountRefunded <= 0)
        {
            return;
        }

        var (purchase, payment) = await FindByPaymentAsync(charge.PaymentIntentId, cancellationToken);

        if (purchase is null)
        {
            return;
        }

        payment?.RecordRefund(charge.AmountRefunded, now);

        if (!charge.Refunded || purchase.PaymentIntentId != charge.PaymentIntentId || purchase.Status == PurchaseStatus.Refunded)
        {
            return;
        }

        if (purchase.SubscriptionId is { } subscriptionId && !purchase.IsFinished)
        {
            await stripe.CancelNowAsync(subscriptionId, cancellationToken);
        }

        purchase.MarkRefunded("stripe", now);
    }

    /// <summary>A dispute of any of the purchase's payments ends access and stops the subscription.</summary>
    private async Task DisputeCreatedAsync(Dispute dispute, DateTime now, CancellationToken cancellationToken)
    {
        if (dispute.PaymentIntentId is null)
        {
            return;
        }

        var (purchase, payment) = await FindByPaymentAsync(dispute.PaymentIntentId, cancellationToken);

        if (purchase is null)
        {
            return;
        }

        payment?.RecordDispute(now);

        if (purchase.SubscriptionId is { } subscriptionId && !purchase.IsFinished)
        {
            await stripe.CancelNowAsync(subscriptionId, cancellationToken);
        }

        purchase.MarkDisputed(now);
    }

    private async Task AccountUpdatedAsync(Account account, DateTime now, CancellationToken cancellationToken)
    {
        var payout = await db.PayoutAccounts.SingleOrDefaultAsync(a => a.StripeAccountId == account.Id, cancellationToken);
        payout?.Update(account.ChargesEnabled, account.PayoutsEnabled, account.DetailsSubmitted, now);
    }

    /// <summary>Finds a purchase by the purchaseId metadata GymHub puts on sessions and subscriptions, else by a Stripe id.</summary>
    private async Task<Purchase?> FindAsync(
        IDictionary<string, string>? metadata, System.Linq.Expressions.Expression<Func<Purchase, bool>> byStripeId, CancellationToken cancellationToken)
    {
        if (metadata is not null && metadata.TryGetValue("purchaseId", out var raw) && Guid.TryParse(raw, out var purchaseId))
        {
            return await db.Purchases.SingleOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);
        }

        return await db.Purchases.SingleOrDefaultAsync(byStripeId, cancellationToken);
    }

    private async Task RecordPaymentAsync(Guid purchaseId, string? paymentIntentId, DateTime now, CancellationToken cancellationToken)
    {
        if (paymentIntentId is null || await db.Payments.AnyAsync(p => p.PaymentIntentId == paymentIntentId, cancellationToken))
        {
            return;
        }

        db.Payments.Add(new PurchasePayment(paymentIntentId, purchaseId, now));
    }

    /// <summary>The purchase a payment belongs to, through its payment record (or, failing that, the latest payment).</summary>
    private async Task<(Purchase? Purchase, PurchasePayment? Payment)> FindByPaymentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.PaymentIntentId == paymentIntentId, cancellationToken);

        var purchase = payment is null
            ? await db.Purchases.SingleOrDefaultAsync(p => p.PaymentIntentId == paymentIntentId, cancellationToken)
            : await db.Purchases.SingleOrDefaultAsync(p => p.Id == payment.PurchaseId, cancellationToken);

        return (purchase, payment);
    }

    private async Task<bool> IsRecordedElsewhereAsync(string eventId)
    {
        db.ChangeTracker.Clear();
        return await db.StripeEvents.AnyAsync(e => e.EventId == eventId);
    }
}

internal static class StripeWebhookEndpoint
{
    public const string SignatureHeader = "Stripe-Signature";

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/webhooks/stripe", async (HttpRequest request, StripeWebhookHandler handler, CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body);
                var json = await reader.ReadToEndAsync(ct);
                var outcome = await handler.Handle(json, request.Headers[SignatureHeader].ToString(), ct);

                return outcome == WebhookOutcome.BadSignature ? Results.BadRequest() : Results.Ok();
            })
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithName("StripeWebhook");
}
