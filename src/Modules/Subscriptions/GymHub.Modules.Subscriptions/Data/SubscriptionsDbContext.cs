using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Subscriptions.Data;

public sealed class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "subscriptions";

    public override string Schema => SchemaName;

    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Enrollment>(enrollment =>
        {
            enrollment.ToTable("enrollments");
            enrollment.HasKey(e => e.Id);
            enrollment.Property(e => e.Id).ValueGeneratedNever();
            enrollment.Property(e => e.UserId).HasMaxLength(100);
            enrollment.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            enrollment.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
            enrollment.HasIndex(e => new { e.UserId, e.ProgramId }).IsUnique();
            enrollment.Ignore(e => e.IsActive);
        });
    }
}
