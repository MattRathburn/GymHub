using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions.Contracts;

namespace GymHub.Modules.Reviews.Features.Shared;

/// <summary>Which programs have reviews, and who may write one.</summary>
internal sealed class ProgramGate(IProgramCatalog catalog, IEntitlements entitlements)
{
    /// <summary>Published and archived programs have reviews; drafts don't exist publicly.</summary>
    public async Task<Result<ProgramSummary>> FindAsync(Guid programId, CancellationToken cancellationToken)
    {
        var program = await catalog.GetSummaryAsync(programId, cancellationToken);

        if (program is null || program.Status == ProgramStatus.Draft)
        {
            return Error.NotFound("Program not found.");
        }

        return program;
    }

    /// <summary>
    /// Writing or editing needs current access to a published program the caller doesn't train. Deleting your own
    /// review never goes through here.
    /// </summary>
    public async Task<Result<ProgramSummary>> CanWriteAsync(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var found = await FindAsync(programId, cancellationToken);

        if (!found.IsSuccess)
        {
            return found;
        }

        var program = found.Value!;

        if (program.TrainerId == user.Id)
        {
            return Error.Forbidden("You can't review your own program.");
        }

        if (program.Status == ProgramStatus.Archived)
        {
            return Error.Conflict("This program is archived, so its reviews are closed.");
        }

        if (!await entitlements.HasAccessAsync(user.Id, programId, cancellationToken))
        {
            return Error.Forbidden("Only members with access to this program can review it.");
        }

        return program;
    }
}
