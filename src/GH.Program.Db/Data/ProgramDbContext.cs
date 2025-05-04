
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GH.Program.Db.Data;

public class ProgramDbContext
{
    /// <remarks>
    /// Add migrations using the following command inside the 'GH.Identity.API' project directory:
    ///
    /// dotnet ef migrations add [migration-name] -o Data/Migrations
    /// </remarks>
    public ProgramDbContext(DbContextOptions<ProgramDbContext> options)
    : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
    }
}