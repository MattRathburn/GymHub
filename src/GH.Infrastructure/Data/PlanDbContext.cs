using GH.Plan.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GH.Plan.Infrastructure.Data;

public class PlanDbContext : DbContext
{
    public PlanDbContext(DbContextOptions<PlanDbContext> options, IConfiguration configuration) : base(options)
    { }

    public DbSet<GHPlan> GHPlans { get; set; }
    public DbSet<PlanComponents> PlanComponents { get; set; }
    public DbSet<PlanCreator> PlanCreators { get; set; }
    public DbSet<UserPlanSubscription> UserPlanSubscriptions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserPlanSubscription>()
            .HasIndex(x => new { x.UserId, x.PlanId })
            .IsUnique();

        modelBuilder.Entity<UserPlanSubscription>()
            .HasOne(x => x.Plan)
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
