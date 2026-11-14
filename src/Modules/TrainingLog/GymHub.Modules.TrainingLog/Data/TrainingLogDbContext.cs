using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.TrainingLog.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.TrainingLog.Data;

public sealed class TrainingLogDbContext(DbContextOptions<TrainingLogDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "training";

    public override string Schema => SchemaName;

    public DbSet<WorkoutSession> Sessions => Set<WorkoutSession>();
    public DbSet<LoggedExercise> Exercises => Set<LoggedExercise>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkoutSession>(session =>
        {
            session.ToTable("sessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).ValueGeneratedNever();
            session.Property(s => s.UserId).HasMaxLength(100);
            session.Property(s => s.Title).HasMaxLength(WorkoutSession.MaxTitleLength);
            session.Property(s => s.Notes).HasMaxLength(5_000);
            session.Property(s => s.ProgramTitle).HasMaxLength(200);
            session.HasIndex(s => new { s.UserId, s.PerformedOn, s.CreatedAt });
            session.HasIndex(s => new { s.UserId, s.ProgramId });
            session.HasMany(s => s.Exercises).WithOne().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoggedExercise>(exercise =>
        {
            exercise.ToTable("exercises");
            exercise.HasKey(e => e.Id);
            exercise.Property(e => e.Id).ValueGeneratedNever();
            exercise.Property(e => e.UserId).HasMaxLength(100);
            exercise.Property(e => e.Name).HasMaxLength(200);
            exercise.Property(e => e.ExerciseKey).HasMaxLength(ExerciseKeys.MaxLength);
            exercise.Property(e => e.Notes).HasMaxLength(2_000);
            exercise.Property(e => e.TopWeightKg).HasPrecision(7, 2);
            exercise.Property(e => e.BestE1RmKg).HasPrecision(7, 2);
            exercise.Property(e => e.VolumeKg).HasPrecision(12, 2);
            exercise.OwnsMany(e => e.Sets, sets => sets.ToJson());
            exercise.HasIndex(e => new { e.UserId, e.ExerciseKey, e.PerformedOn });
        });
    }
}
