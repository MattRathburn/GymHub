using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Notifications.Data;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "notifications";

    public override string Schema => SchemaName;

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<EmailMessage> Emails => Set<EmailMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("notifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.UserId).HasMaxLength(100);
            notification.Property(n => n.Type).HasConversion<string>().HasMaxLength(40);
            notification.Property(n => n.Title).HasMaxLength(Notification.MaxTitleLength);
            notification.Property(n => n.Body).HasMaxLength(Notification.MaxBodyLength);
            notification.Property(n => n.Link).HasMaxLength(500);
            notification.Property(n => n.DedupeKey).HasMaxLength(200);
            notification.Ignore(n => n.IsRead);
            notification.HasIndex(n => new { n.UserId, n.DedupeKey }).IsUnique();
            notification.HasIndex(n => new { n.UserId, n.ShowInApp, n.ReadAt, n.CreatedAt });
            notification.HasIndex(n => n.CreatedAt);
        });

        modelBuilder.Entity<NotificationPreference>(preference =>
        {
            preference.ToTable("preferences");
            preference.HasKey(p => new { p.UserId, p.Type });
            preference.Property(p => p.UserId).HasMaxLength(100);
            preference.Property(p => p.Type).HasConversion<string>().HasMaxLength(40);
        });

        modelBuilder.Entity<Recipient>(recipient =>
        {
            recipient.ToTable("recipients");
            recipient.HasKey(r => r.UserId);
            recipient.Property(r => r.UserId).HasMaxLength(100);
            recipient.Property(r => r.Email).HasMaxLength(320);
        });

        modelBuilder.Entity<EmailMessage>(email =>
        {
            email.ToTable("emails");
            email.HasKey(e => e.Id);
            email.Property(e => e.Id).ValueGeneratedNever();
            email.Property(e => e.ToAddress).HasMaxLength(320);
            email.Property(e => e.Subject).HasMaxLength(300);
            email.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            email.Property(e => e.LastError).HasMaxLength(1000);
            email.HasIndex(e => new { e.Status, e.NextAttemptAt });
            email.HasIndex(e => e.CreatedAt);
        });
    }
}
