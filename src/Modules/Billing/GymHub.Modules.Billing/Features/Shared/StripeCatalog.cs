using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Payments;
using GymHub.Modules.Programs.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Billing.Features.Shared;

/// <summary>A Stripe-hosted page to send the browser to.</summary>
public sealed record RedirectDto(Uri Url);

/// <summary>Finds or creates the Stripe customer for a member and the Stripe price for a program price version.</summary>
internal sealed class StripeCatalog(BillingDbContext db, IStripeGateway stripe)
{
    public async Task<string> CustomerIdAsync(string userId, string? email, CancellationToken cancellationToken)
    {
        var existing = await db.Customers.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            return existing.StripeCustomerId;
        }

        var customerId = await stripe.CreateCustomerAsync(userId, email, cancellationToken);
        db.Customers.Add(new BillingCustomer(userId, customerId));
        await db.SaveChangesAsync(cancellationToken);
        return customerId;
    }

    public async Task<string> PriceIdAsync(Guid programId, string title, int priceVersion, ProgramPrice price, CancellationToken cancellationToken)
    {
        var prices = await db.CatalogPrices.Where(p => p.ProgramId == programId).ToListAsync(cancellationToken);

        if (prices.SingleOrDefault(p => p.PriceVersion == priceVersion) is { } existing)
        {
            return existing.StripePriceId;
        }

        var productId = prices.FirstOrDefault()?.StripeProductId ?? await stripe.CreateProductAsync(programId, title, cancellationToken);
        var priceId = await stripe.CreatePriceAsync(productId, programId, priceVersion, price, cancellationToken);

        db.CatalogPrices.Add(new CatalogPrice(programId, priceVersion, productId, priceId));
        await db.SaveChangesAsync(cancellationToken);
        return priceId;
    }
}

internal sealed class PayoutReadiness(BillingDbContext db) : Contracts.IPayoutReadiness
{
    public Task<bool> IsReadyAsync(string trainerId, CancellationToken cancellationToken) =>
        db.PayoutAccounts.AnyAsync(a => a.TrainerId == trainerId && a.ChargesEnabled, cancellationToken);
}

internal static class OpenCheckouts
{
    /// <summary>
    /// Expires the Stripe sessions of these purchases that could still be paid, so a stale checkout (an older price,
    /// an archived program, a superseded attempt) can't be paid. False when one was paid in the meantime.
    /// </summary>
    public static async Task<bool> CloseAsync(this IStripeGateway stripe, IEnumerable<Purchase> purchases, DateTime now, CancellationToken cancellationToken)
    {
        var allClosed = true;

        foreach (var purchase in purchases.Where(p => p.CheckoutMayBePaid(now)))
        {
            if (await stripe.ExpireCheckoutAsync(purchase.CheckoutSessionId!, cancellationToken))
            {
                purchase.CloseCheckout();
            }
            else
            {
                allClosed = false;
            }
        }

        return allClosed;
    }
}

internal static class StripeFailures
{
    /// <summary>Stripe's message, for errors the user can act on (e.g. a currency the customer can't use).</summary>
    public static GymHub.BuildingBlocks.Results.Error ToError(this Stripe.StripeException ex) =>
        GymHub.BuildingBlocks.Results.Error.Conflict($"Payment provider error: {ex.StripeError?.Message ?? ex.Message}");
}
