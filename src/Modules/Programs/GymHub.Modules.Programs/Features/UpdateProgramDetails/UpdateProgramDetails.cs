using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.UpdateProgramDetails;

internal sealed class UpdateProgramDetailsHandler(ProgramsDbContext db)
{
    public async Task<Result> Handle(Guid programId, ProgramDetailsRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var result = loaded.Value!.UpdateDetails(request.Title, request.Summary, request.Description, request.Kind, request.StartsOn, request.EndsOn);

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

internal static class UpdateProgramDetailsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/{programId:guid}", async (Guid programId, ProgramDetailsRequest request, ClaimsPrincipal user, UpdateProgramDetailsHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<ProgramDetailsRequest>()
            .RequireAuthorization(Policies.Trainer)
            .WithName("UpdateProgramDetails");
}
