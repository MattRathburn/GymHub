using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions.Contracts;

namespace GymHub.Modules.Discussions.Features.Shared;

/// <summary>What the caller may do on one program's board.</summary>
/// <param name="CanPost">The program is still published (an archived program's board is read-only).</param>
/// <param name="CanModerate">The caller is the program's trainer or an admin.</param>
internal sealed record Board(Guid ProgramId, string ProgramTitle, string TrainerId, bool CanPost, bool CanModerate);

/// <summary>
/// The board is for the program's subscribers, its trainer and admins. Checked on every request (and hub join),
/// so unsubscribing removes access straight away.
/// </summary>
internal sealed class BoardAccess(IProgramCatalog catalog, IEntitlements entitlements)
{
    public async Task<Result<Board>> OpenAsync(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var program = await catalog.GetSummaryAsync(programId, cancellationToken);

        if (program is null || program.Status == ProgramStatus.Draft)
        {
            return Error.NotFound("Program not found.");
        }

        var canModerate = program.TrainerId == user.Id || user.IsAdmin;

        if (!canModerate && !await entitlements.HasAccessAsync(user.Id, programId, cancellationToken))
        {
            return Error.Forbidden("Only the program's subscribers can see its discussion board.");
        }

        return new Board(programId, program.Title, program.TrainerId, program.Status == ProgramStatus.Published, canModerate);
    }
}
