using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Features.Shared;
using GymHub.Modules.Billing.Payments;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Billing;

/// <summary>
/// Purchases are kept for accounting. Once the identity, profiles and Stripe customer are gone, their user id no longer points at a
/// person.
/// </summary>
internal sealed class BillingPersonalData(BillingDbContext db, IStripeGateway stripe, TimeProvider timeProvider) : IPersonalDataSource
{
    private static readonly PurchaseStatus[] Renewing = [PurchaseStatus.Trialing, PurchaseStatus.Active, PurchaseStatus.PastDue];

    public string Section => "billing";

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken)
    {
        var purchases = await db.Purchases.AsNoTracking()
            .Where(p => p.UserId == userId && p.Status != PurchaseStatus.Pending)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id, p.ProgramId, p.Kind, p.Status, p.AmountMinor, p.Currency, p.Interval, p.CreatedAt, p.LastPaymentAt,
                p.CurrentPeriodEnd, p.CancelAtPeriodEnd, p.EndedAt
            })
            .ToListAsync(cancellationToken);

        var payoutAccount = await db.PayoutAccounts.AsNoTracking()
            .Where(a => a.TrainerId == userId)
            .Select(a => new { a.StripeAccountId, a.ChargesEnabled, a.PayoutsEnabled })
            .SingleOrDefaultAsync(cancellationToken);

        return new { purchases, payoutAccount };
    }

    public async Task<string?> GetDeletionBlockerAsync(string userId, CancellationToken cancellationToken)
    {
        var renewing = await db.Purchases.AnyAsync(p => p.UserId == userId
            && p.Kind == PurchaseKind.Subscription
            && Renewing.Contains(p.Status)
            && !p.CancelAtPeriodEnd, cancellationToken);

        return renewing ? "You have a paid subscription that still renews. Cancel it from Manage billing first." : null;
    }

    /// <summary>
    /// Closes open checkouts, so none can be paid after the account is gone, then deletes the Stripe customer (its
    /// email and saved cards) and our link to it.
    /// </summary>
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        var pending = await db.Purchases.Where(p => p.UserId == userId && p.Status == PurchaseStatus.Pending).ToListAsync(cancellationToken);

        if (!await stripe.CloseAsync(pending, timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
        {
            // Paid just now: stop, so the next run sees the new subscription as a blocker instead of erasing under it.
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException($"A checkout of user {userId} was paid during account deletion.");
        }

        await db.SaveChangesAsync(cancellationToken);

        if (await db.Customers.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken) is { } customer)
        {
            await stripe.DeleteCustomerAsync(customer.StripeCustomerId, cancellationToken);
            db.Customers.Remove(customer);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
