using System.ComponentModel.DataAnnotations;

namespace GH.Program.API.Models;

public class GHProgram
{
    public int Id { get; set; }

    [Required]
    public string? Name { get; set; }

    public string? Description { get; set; }

    public ProgramCreator Creator { get; set; }
    public IList<ProgramComponents> Components { get; set; }

}