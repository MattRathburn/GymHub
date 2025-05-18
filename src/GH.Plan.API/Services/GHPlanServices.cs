
using GH.Plan.Infrastructure.Data;

namespace GH.Plan.API.Services;

public class GHPlanServices(
    PlanDbContext context,
    ILogger<GHPlanServices> logger)
{
    public PlanDbContext Context { get; } = context;
    public ILogger<GHPlanServices> Logger { get; } = logger;
}
