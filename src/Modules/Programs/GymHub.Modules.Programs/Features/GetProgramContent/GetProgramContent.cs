using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Features.Shared;
using GymHub.Modules.Subscriptions.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.GetProgramContent;

/// <summary>Released weeks with full workouts, for subscribers, the program's trainer and admins.</summary>
internal sealed class GetProgramContentHandler(ProgramsDbContext db, IEntitlements entitlements, ProgramContentMapper mapper)
{
    public async Task<Result<ProgramContentDto>> Handle(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var program = await db.LoadProgramAsync(programId, cancellationToken);

        if (program is null || (program.Status == ProgramStatus.Draft && !program.IsOwnedBy(user.Id)))
        {
            return Error.NotFound("Program not found.");
        }

        var allowed = program.IsOwnedBy(user.Id)
            || user.IsAdmin
            || await entitlements.HasAccessAsync(user.Id, programId, cancellationToken);

        if (!allowed)
        {
            return Error.Forbidden("Subscribe to this program to see its content.");
        }

        return mapper.ToContent(program, program.ReleasedWeeks(mapper.Today));
    }
}

internal static class GetProgramContentEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{programId:guid}/content", async (Guid programId, ClaimsPrincipal user, GetProgramContentHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("GetProgramContent");
}
