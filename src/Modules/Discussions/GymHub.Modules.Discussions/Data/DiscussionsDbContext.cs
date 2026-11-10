using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Discussions.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions.Data;

public sealed class DiscussionsDbContext(DbContextOptions<DiscussionsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "discussions";

    public override string Schema => SchemaName;

    public DbSet<DiscussionThread> Threads => Set<DiscussionThread>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostReport> Reports => Set<PostReport>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var thread in ChangeTracker.Entries<DiscussionThread>().Select(e => e.Entity).ToList())
        {
            foreach (var @event in thread.Events)
            {
                AddIntegrationEvent(@event);
            }

            thread.ClearEvents();
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DiscussionThread>(thread =>
        {
            thread.ToTable("threads");
            thread.HasKey(t => t.Id);
            thread.Property(t => t.Id).ValueGeneratedNever();
            thread.Property(t => t.AuthorId).HasMaxLength(100);
            thread.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20);
            thread.Property(t => t.Title).HasMaxLength(DiscussionThread.MaxTitleLength);
            thread.Ignore(t => t.IsPinned);
            thread.Ignore(t => t.IsLocked);
            thread.Ignore(t => t.IsHidden);
            thread.HasIndex(t => new { t.ProgramId, t.LastActivityAt });
        });

        modelBuilder.Entity<Post>(post =>
        {
            post.ToTable("posts");
            post.HasKey(p => p.Id);
            post.Property(p => p.Id).ValueGeneratedNever();
            post.Property(p => p.AuthorId).HasMaxLength(100);
            post.Property(p => p.AuthorName).HasMaxLength(Post.MaxAuthorNameLength);
            post.Property(p => p.Body).HasMaxLength(Post.MaxBodyLength);
            post.Property(p => p.HiddenBy).HasMaxLength(100);
            post.Ignore(p => p.IsOpeningPost);
            post.Ignore(p => p.IsDeleted);
            post.Ignore(p => p.IsHidden);
            post.HasIndex(p => new { p.ThreadId, p.CreatedAt });
            post.HasOne<DiscussionThread>().WithMany().HasForeignKey(p => p.ThreadId).OnDelete(DeleteBehavior.Cascade);

            // Only leaf posts are ever deleted outright; a post with replies becomes a placeholder. NO ACTION (checked at
            // the end of the statement) rather than RESTRICT, so a thread's cascade can remove a parent and its replies together.
            post.HasOne<Post>().WithMany().HasForeignKey(p => p.ParentPostId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PostReport>(report =>
        {
            report.ToTable("reports");
            report.HasKey(r => r.Id);
            report.Property(r => r.Id).ValueGeneratedNever();
            report.Property(r => r.ReporterId).HasMaxLength(100);
            report.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20);
            report.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            report.Property(r => r.Note).HasMaxLength(PostReport.MaxNoteLength);
            report.Property(r => r.ResolvedBy).HasMaxLength(100);
            report.HasIndex(r => new { r.Status, r.CreatedAt });
            report.HasOne<Post>().WithMany().HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);

            // One open report per reporter per post, even under concurrent submits.
            report.HasIndex(r => new { r.PostId, r.ReporterId })
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'");
        });
    }
}
