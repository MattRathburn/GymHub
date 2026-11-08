using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;

namespace GymHub.Modules.Programs.Features.Shared;

internal static class OwnedProgram
{
    /// <summary>Loads a program for editing by its trainer. Other users get Forbidden.</summary>
    public static async Task<Result<TrainingProgram>> LoadOwnedAsync(this ProgramsDbContext db, Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var program = await db.LoadProgramAsync(programId, cancellationToken);

        if (program is null)
        {
            return Error.NotFound("Program not found.");
        }

        if (!program.IsOwnedBy(user.Id))
        {
            return Error.Forbidden("Only the program's trainer can change it.");
        }

        return program;
    }

    public static DateTime UtcNow(this TimeProvider timeProvider) => timeProvider.GetUtcNow().UtcDateTime;
}
