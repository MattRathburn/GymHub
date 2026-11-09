using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.CreateProgram;

public sealed record CreateProgramResponse(Guid Id);

internal sealed class CreateProgramHandler(ProgramsDbContext db, TimeProvider timeProvider)
{
    public async Task<Result<CreateProgramResponse>> Handle(ProgramDetailsRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var created = TrainingProgram.Create(
            user.Id, request.Title, request.Summary, request.Description,
            request.Kind, request.StartsOn, request.EndsOn, timeProvider.UtcNow());

        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Programs.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateProgramResponse(created.Value!.Id);
    }
}

internal static class CreateProgramEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (ProgramDetailsRequest request, ClaimsPrincipal user, CreateProgramHandler handler, CancellationToken ct) =>
                (await handler.Handle(request, CurrentUser.Required(user), ct))
                    .ToHttp(r => TypedResults.Created($"/api/v1/programs/{r.Id}", r)))
            .WithValidation<ProgramDetailsRequest>()
            .RequireAuthorization(Policies.Trainer)
            .WithName("CreateProgram");
}
