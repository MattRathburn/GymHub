using GH.Plan.Infrastructure.Data;

namespace GH.Admin.Services;

public class GHAdminServices(
    PlanDbContext context,
    ILogger<GHAdminServices> logger)
{
    public PlanDbContext Context { get; } = context;
    public ILogger<GHAdminServices> Logger { get; } = logger;
}
