using GymHub.BuildingBlocks.Admin;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Billing;

/// <summary>
/// Sales and platform fees from the payments of the last 30 days, per currency, grouped in SQL. A payment has no
/// amount of its own, so each counts at its purchase's amount (the current one, after any price change), less what
/// was refunded. Disputed payments don't count: the bank took the money back.
/// </summary>
internal sealed class BillingMetrics(BillingDbContext db) : IAdminMetricsSource
{
    public async Task<IReadOnlyList<AdminMetric>> GetMetricsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var since = now - AdminMetricsExtensions.RecentWindow;

        // Grouped in SQL by currency, fee rate and net amount: few rows (there are few distinct prices), and each
        // payment's fee is then rounded exactly as Stripe was charged it (Purchase.ApplicationFee).
        var groups = await db.Payments.AsNoTracking()
            .Where(pay => pay.PaidAt >= since && pay.DisputedAt == null)
            .Join(db.Purchases, pay => pay.PurchaseId, p => p.Id,
                (pay, p) => new { p.Currency, p.FeePercent, Net = p.AmountMinor - pay.RefundedAmountMinor })
            .Where(x => x.Net > 0)
            .GroupBy(x => new { x.Currency, x.FeePercent, x.Net })
            .Select(g => new { g.Key.Currency, g.Key.FeePercent, g.Key.Net, Payments = g.LongCount() })
            .ToListAsync(cancellationToken);

        var byCurrency = groups
            .GroupBy(g => g.Currency)
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => new
            {
                Currency = c.Key,
                Sales = c.Sum(g => g.Net * g.Payments),
                Fees = c.Sum(g => Purchase.ApplicationFee(g.Net, g.FeePercent) * g.Payments)
            })
            .ToList();

        if (byCurrency.Count == 0)
        {
            return [new(AdminSections.Money, "Sales (30 days)", 0, Link: "/admin/purchases")];
        }

        return
        [
            .. byCurrency.Select(c => new AdminMetric(AdminSections.Money, "Sales (30 days)", c.Sales, c.Currency, "/admin/purchases")),
            .. byCurrency.Select(c => new AdminMetric(AdminSections.Money, "Platform fees (30 days)", c.Fees, c.Currency))
        ];
    }
}
