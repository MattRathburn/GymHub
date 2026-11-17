using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Reviews.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews.Data;

public sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "reviews";

    public override string Schema => SchemaName;

    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewReport> Reports => Set<ReviewReport>();
    public DbSet<ProgramRating> Ratings => Set<ProgramRating>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Review>(review =>
        {
            review.ToTable("reviews");
            review.HasKey(r => r.Id);
            review.Property(r => r.Id).ValueGeneratedNever();
            review.Property(r => r.TrainerId).HasMaxLength(100);
            review.Property(r => r.AuthorId).HasMaxLength(100);
            review.Property(r => r.AuthorName).HasMaxLength(Review.MaxNameLength);
            review.Property(r => r.Body).HasMaxLength(Review.MaxBodyLength);
            review.Property(r => r.HiddenBy).HasMaxLength(100);
            review.Property(r => r.Reply).HasMaxLength(Review.MaxReplyLength);
            review.HasIndex(r => new { r.ProgramId, r.AuthorId }).IsUnique();
            review.HasIndex(r => new { r.ProgramId, r.IsHidden, r.CreatedAt });
        });

        modelBuilder.Entity<ReviewReport>(report =>
        {
            report.ToTable("review_reports");
            report.HasKey(r => r.Id);
            report.Property(r => r.Id).ValueGeneratedNever();
            report.Property(r => r.ReporterId).HasMaxLength(100);
            report.Property(r => r.ResolvedBy).HasMaxLength(100);
            report.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20);
            report.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            report.Property(r => r.Note).HasMaxLength(ReviewReport.MaxNoteLength);
            report.HasIndex(r => new { r.Status, r.CreatedAt });
            report.HasOne<Review>().WithMany().HasForeignKey(r => r.ReviewId).OnDelete(DeleteBehavior.Cascade);

            // One open report per reporter per review, even under concurrent submits.
            report.HasIndex(r => new { r.ReviewId, r.ReporterId })
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'");
        });

        modelBuilder.Entity<ProgramRating>(rating =>
        {
            rating.ToTable("program_ratings");
            rating.HasKey(r => r.ProgramId);
            rating.Property(r => r.ProgramId).ValueGeneratedNever();
            rating.Ignore(r => r.Average);
        });
    }
}
