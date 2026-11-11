using EventBus.Abstractions;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Features.Shared;
using GymHub.Modules.Billing.Payments;
using GymHub.Modules.Programs.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Billing.PriceChanges;

/// <summary>
/// A published program's price changed. Subscribers move to a new recurring price at their next renewal; if the
/// program became free they keep access as free members; if it became one-time their subscription runs out.
/// One-time buyers are unaffected. Idempotent: each purchase records what was already done.
/// </summary>
internal sealed class ProgramPricingChangedHandler(
    BillingDbContext db, IProgramCatalog catalog, StripeCatalog stripeCatalog, IStripeGateway stripe, TimeProvider timeProvider,
    ILogger<ProgramPricingChangedHandler> logger)
    : IIntegrationEventHandler<ProgramPricingChanged>
{
    public async Task Handle(ProgramPricingChanged @event)
    {
        var price = @event.Price;

        // Checkouts started at an older price must not be paid at it.
        var checkouts = await db.Purchases
            .Where(p => p.ProgramId == @event.ProgramId && p.Status == PurchaseStatus.Pending && p.PriceVersion < @event.PriceVersion)
            .ToListAsync();
        await CloseCheckoutsAsync(db, stripe, checkouts, timeProvider, logger, @event.ProgramId);

        var subscriptions = await ActiveSubscriptions(db, @event.ProgramId)
            .Where(p => p.PriceVersion < @event.PriceVersion && (p.ScheduledPriceVersion == null || p.ScheduledPriceVersion < @event.PriceVersion))
            .ToListAsync();

        if (subscriptions.Count == 0)
        {
            return;
        }

        string? priceId = null;

        if (price.Model == PricingModel.Recurring)
        {
            var program = await catalog.GetSummaryAsync(@event.ProgramId, CancellationToken.None);
            priceId = await stripeCatalog.PriceIdAsync(@event.ProgramId, program?.Title ?? "Program", @event.PriceVersion, price, CancellationToken.None);
        }

        foreach (var purchase in subscriptions)
        {
            switch (price.Model)
            {
                case PricingModel.Free:
                    // Also for a subscription that is already ending, so its buyer keeps access once it runs out.
                    if (!purchase.CancelAtPeriodEnd)
                    {
                        await stripe.CancelAtPeriodEndAsync(purchase.SubscriptionId!, CancellationToken.None);
                    }

                    purchase.ConvertToFree();
                    break;
                case var _ when purchase.CancelAtPeriodEnd:
                    // Already ending; nothing to move.
                    continue;
                case PricingModel.Recurring when price.Currency == purchase.Currency:
                    await stripe.SchedulePriceChangeAsync(purchase.SubscriptionId!, priceId!, price.Interval!.Value, CancellationToken.None);
                    purchase.SchedulePriceChange(@event.PriceVersion, price.AmountMinor!.Value, price.Interval.Value);
                    break;
                default:
                    await stripe.CancelAtPeriodEndAsync(purchase.SubscriptionId!, CancellationToken.None);
                    purchase.StopRenewing();
                    break;
            }

            // Save each one, so a failure part-way doesn't redo the Stripe calls already made.
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Applied price version {Version} of program {ProgramId} to {Count} subscriptions",
            @event.PriceVersion, @event.ProgramId, subscriptions.Count);
    }

    internal static async Task CloseCheckoutsAsync(
        BillingDbContext db, IStripeGateway stripe, List<Purchase> pending, TimeProvider timeProvider, ILogger logger, Guid programId)
    {
        if (!await stripe.CloseAsync(pending, timeProvider.GetUtcNow().UtcDateTime, CancellationToken.None))
        {
            logger.LogWarning("A checkout for program {ProgramId} was paid before it could be closed", programId);
        }

        await db.SaveChangesAsync();
    }

    internal static IQueryable<Purchase> ActiveSubscriptions(BillingDbContext db, Guid programId) =>
        db.Purchases.Where(p => p.ProgramId == programId
            && p.Kind == PurchaseKind.Subscription
            && p.SubscriptionId != null
            && (p.Status == PurchaseStatus.Trialing || p.Status == PurchaseStatus.Active || p.Status == PurchaseStatus.PastDue));
}

/// <summary>An archived program takes no more renewals or new buyers; subscribers keep access until their period ends.</summary>
internal sealed class ProgramArchivedHandler(BillingDbContext db, IStripeGateway stripe, TimeProvider timeProvider, ILogger<ProgramArchivedHandler> logger)
    : IIntegrationEventHandler<ProgramArchived>
{
    public async Task Handle(ProgramArchived @event)
    {
        var checkouts = await db.Purchases.Where(p => p.ProgramId == @event.ProgramId && p.Status == PurchaseStatus.Pending).ToListAsync();
        await ProgramPricingChangedHandler.CloseCheckoutsAsync(db, stripe, checkouts, timeProvider, logger, @event.ProgramId);

        var subscriptions = await ProgramPricingChangedHandler.ActiveSubscriptions(db, @event.ProgramId)
            .Where(p => !p.CancelAtPeriodEnd)
            .ToListAsync();

        foreach (var purchase in subscriptions)
        {
            await stripe.CancelAtPeriodEndAsync(purchase.SubscriptionId!, CancellationToken.None);
            purchase.StopRenewing();
            await db.SaveChangesAsync();
        }
    }
}
