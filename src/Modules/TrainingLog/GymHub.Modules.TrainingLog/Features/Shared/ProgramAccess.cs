using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions.Contracts;

namespace GymHub.Modules.TrainingLog.Features.Shared;

/// <summary>The same rule as program content: the program's trainer, an admin, or a member with access.</summary>
internal sealed class ProgramAccess(IProgramCatalog catalog, IEntitlements entitlements)
{
    public async Task<Result> CheckAsync(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var program = await catalog.GetSummaryAsync(programId, cancellationToken);

        if (program is null || program.Status == ProgramStatus.Draft)
        {
            return Error.NotFound("Program not found.");
        }

        if (program.TrainerId == user.Id || user.IsAdmin || await entitlements.HasAccessAsync(user.Id, programId, cancellationToken))
        {
            return Result.Success();
        }

        return Error.Forbidden("Join this program to log its workouts.");
    }
}
