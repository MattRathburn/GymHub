namespace GH.Plan.Infrastructure.Models;

public class PlanComponents
{
    public int Id { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Plan Component must have a name.")]
    public required string Name { get; set; }
    public string? Description { get; set; }
}

