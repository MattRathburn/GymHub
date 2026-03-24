using System.ComponentModel.DataAnnotations;

namespace GH.Plan.Infrastructure.Models;

public class GHPlan
{
    public int Id { get; set; }

    [Required]
    public required string? Name { get; set; }

    public string? Description { get; set; }

    public PlanCreator? Creator { get; set; }
    public IList<PlanComponents>? Components { get; set; }

}