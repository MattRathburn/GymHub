using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.PublishProgram;

internal sealed class PublishProgramHandler(ProgramsDbContext db, TimeProvider timeProvider)
{
    public async Task<Result> Handle(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var result = loaded.Value!.Publish(timeProvider.UtcNow());

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

internal static class PublishProgramEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{programId:guid}/publish", async (Guid programId, ClaimsPrincipal user, PublishProgramHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization(Policies.Trainer)
            .WithName("PublishProgram");
}
