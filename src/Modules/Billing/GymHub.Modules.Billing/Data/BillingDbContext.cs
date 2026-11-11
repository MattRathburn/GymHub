using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Billing.Data;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "billing";

    public override string Schema => SchemaName;

    public DbSet<PayoutAccount> PayoutAccounts => Set<PayoutAccount>();
    public DbSet<BillingCustomer> Customers => Set<BillingCustomer>();
    public DbSet<CatalogPrice> CatalogPrices => Set<CatalogPrice>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchasePayment> Payments => Set<PurchasePayment>();
    public DbSet<StripeEventRecord> StripeEvents => Set<StripeEventRecord>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var purchase in ChangeTracker.Entries<Purchase>().Select(e => e.Entity).ToList())
        {
            foreach (var @event in purchase.Events)
            {
                AddIntegrationEvent(@event);
            }

            purchase.ClearEvents();
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PayoutAccount>(account =>
        {
            account.ToTable("payout_accounts");
            account.HasKey(a => a.TrainerId);
            account.Property(a => a.TrainerId).HasMaxLength(100);
            account.Property(a => a.StripeAccountId).HasMaxLength(100);
            account.HasIndex(a => a.StripeAccountId).IsUnique();
            account.Ignore(a => a.IsReady);
        });

        modelBuilder.Entity<BillingCustomer>(customer =>
        {
            customer.ToTable("customers");
            customer.HasKey(c => c.UserId);
            customer.Property(c => c.UserId).HasMaxLength(100);
            customer.Property(c => c.StripeCustomerId).HasMaxLength(100);
        });

        modelBuilder.Entity<CatalogPrice>(price =>
        {
            price.ToTable("catalog_prices");
            price.HasKey(p => new { p.ProgramId, p.PriceVersion });
            price.Property(p => p.StripeProductId).HasMaxLength(100);
            price.Property(p => p.StripePriceId).HasMaxLength(100);
            price.HasIndex(p => p.StripePriceId).IsUnique();
        });

        modelBuilder.Entity<Purchase>(purchase =>
        {
            purchase.ToTable("purchases");
            purchase.HasKey(p => p.Id);
            purchase.Property(p => p.Id).ValueGeneratedNever();
            purchase.Property(p => p.UserId).HasMaxLength(100);
            purchase.Property(p => p.TrainerId).HasMaxLength(100);
            purchase.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
            purchase.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            purchase.Property(p => p.Currency).HasMaxLength(3);
            purchase.Property(p => p.Interval).HasConversion<string>().HasMaxLength(20);
            purchase.Property(p => p.ScheduledInterval).HasConversion<string>().HasMaxLength(20);
            purchase.Property(p => p.FeePercent).HasPrecision(5, 2);
            purchase.Property(p => p.CheckoutSessionId).HasMaxLength(200);
            purchase.Property(p => p.CheckoutUrl).HasMaxLength(2000);
            purchase.Property(p => p.PaymentIntentId).HasMaxLength(100);
            purchase.Property(p => p.SubscriptionId).HasMaxLength(100);
            purchase.Property(p => p.StripeStatus).HasMaxLength(30);
            purchase.Property(p => p.RefundedBy).HasMaxLength(100);
            purchase.Ignore(p => p.GivesAccess);
            purchase.Ignore(p => p.IsFinished);
            purchase.Ignore(p => p.FeeMinor);
            purchase.HasIndex(p => new { p.UserId, p.ProgramId });
            purchase.HasIndex(p => new { p.TrainerId, p.CreatedAt });
            purchase.HasIndex(p => p.ProgramId);
            purchase.HasIndex(p => p.CheckoutSessionId);
            purchase.HasIndex(p => p.SubscriptionId);
            purchase.HasIndex(p => p.PaymentIntentId);
            purchase.Property(p => p.Version).IsRowVersion();
        });

        modelBuilder.Entity<PurchasePayment>(payment =>
        {
            payment.ToTable("purchase_payments");
            payment.HasKey(p => p.PaymentIntentId);
            payment.Property(p => p.PaymentIntentId).HasMaxLength(100);
            payment.HasIndex(p => p.PurchaseId);
            payment.HasOne<Purchase>().WithMany().HasForeignKey(p => p.PurchaseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StripeEventRecord>(record =>
        {
            record.ToTable("stripe_events");
            record.HasKey(r => r.EventId);
            record.Property(r => r.EventId).HasMaxLength(100);
            record.Property(r => r.Type).HasMaxLength(100);
        });
    }
}
