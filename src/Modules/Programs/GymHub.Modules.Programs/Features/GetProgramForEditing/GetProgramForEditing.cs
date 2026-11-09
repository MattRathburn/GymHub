using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.GetProgramForEditing;

/// <summary>Everything in the program, released or not, for its trainer's editor.</summary>
internal sealed class GetProgramForEditingHandler(ProgramsDbContext db, ProgramContentMapper mapper)
{
    public async Task<Result<ProgramContentDto>> Handle(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        return mapper.ToContent(loaded.Value!, loaded.Value!.Weeks);
    }
}

internal static class GetProgramForEditingEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{programId:guid}/edit", async (Guid programId, ClaimsPrincipal user, GetProgramForEditingHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization(Policies.Trainer)
            .WithName("GetProgramForEditing");
}
