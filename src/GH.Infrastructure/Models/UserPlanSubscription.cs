namespace GH.Plan.Infrastructure.Models;

public class UserPlanSubscription
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int PlanId { get; set; }

    public GHPlan? Plan { get; set; }
}
