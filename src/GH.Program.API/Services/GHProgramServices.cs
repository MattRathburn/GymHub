using GH.Program.API.Infrastructure;
using GH.Program.API.Models;
using Microsoft.Extensions.Options;

namespace GH.Program.API.Services;

public class GHProgramServices(
    ProgramDbContext context,
    ILogger<GHProgramServices> logger)
{
    public ProgramDbContext Context { get; } = context;
    public ILogger<GHProgramServices> Logger { get; } = logger;
}
