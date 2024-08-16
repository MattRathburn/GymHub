using GH.Program.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GH.Program.API.Infrastructure;

public class ProgramDbContext : DbContext
{
    public ProgramDbContext(DbContextOptions<ProgramDbContext> options, IConfiguration configuration) : base(options)
    { }

    public DbSet<GHProgram> GHPrograms { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        base.OnModelCreating(modelBuilder);
    }
}
