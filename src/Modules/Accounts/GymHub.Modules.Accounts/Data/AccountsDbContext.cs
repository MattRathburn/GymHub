using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Accounts.Data;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "accounts";

    public override string Schema => SchemaName;

    public DbSet<AccountDeletion> Deletions => Set<AccountDeletion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AccountDeletion>(deletion =>
        {
            deletion.ToTable("account_deletions");
            deletion.HasKey(d => d.UserId);
            deletion.Property(d => d.UserId).HasMaxLength(100);
            deletion.HasIndex(d => new { d.CompletedAt, d.ScheduledFor });
        });
    }
}
