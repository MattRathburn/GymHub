using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Trainers.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Data;

public sealed class TrainersDbContext(DbContextOptions<TrainersDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "trainers";

    public override string Schema => SchemaName;

    public DbSet<TrainerApplication> Applications => Set<TrainerApplication>();
    public DbSet<TrainerProfile> Profiles => Set<TrainerProfile>();
    public DbSet<Follow> Follows => Set<Follow>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var application in ChangeTracker.Entries<TrainerApplication>().Select(e => e.Entity).ToList())
        {
            foreach (var @event in application.Events)
            {
                AddIntegrationEvent(@event);
            }

            application.ClearEvents();
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrainerApplication>(application =>
        {
            application.ToTable("applications");
            application.HasKey(a => a.Id);
            application.Property(a => a.Id).ValueGeneratedNever();
            application.Property(a => a.UserId).HasMaxLength(100);
            application.Property(a => a.DisplayName).HasMaxLength(80);
            application.Property(a => a.Bio).HasMaxLength(2000);
            application.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            application.Property(a => a.ReviewedBy).HasMaxLength(100);
            application.Property(a => a.RejectionReason).HasMaxLength(500);
            application.Ignore(a => a.Details);
            application.HasIndex(a => new { a.Status, a.SubmittedAt });

            // At most one pending application per user, even under concurrent submits.
            application.HasIndex(a => a.UserId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Pending'");
        });

        modelBuilder.Entity<TrainerProfile>(profile =>
        {
            profile.ToTable("profiles");
            profile.HasKey(p => p.UserId);
            profile.Property(p => p.UserId).HasMaxLength(100);
            profile.Property(p => p.DisplayName).HasMaxLength(80);
            profile.Property(p => p.Bio).HasMaxLength(2000);
            profile.Property(p => p.AvatarObjectKey).HasMaxLength(300);
        });

        modelBuilder.Entity<Follow>(follow =>
        {
            follow.ToTable("follows");
            follow.HasKey(f => new { f.UserId, f.TrainerId });
            follow.Property(f => f.UserId).HasMaxLength(100);
            follow.Property(f => f.TrainerId).HasMaxLength(100);
            follow.HasIndex(f => f.TrainerId);
            follow.HasOne<TrainerProfile>().WithMany().HasForeignKey(f => f.TrainerId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
