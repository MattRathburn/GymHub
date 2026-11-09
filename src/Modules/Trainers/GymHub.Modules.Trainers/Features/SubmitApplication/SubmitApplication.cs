using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Domain;
using GymHub.Modules.Trainers.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Features.SubmitApplication;

/// <summary>A signed-in user applies to become a trainer, and checks on their latest application.</summary>
internal sealed class SubmitApplicationHandler(TrainersDbContext db, TimeProvider timeProvider)
{
    public async Task<Result<ApplicationDto>> Submit(TrainerDetailsRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var previous = await db.Applications.Where(a => a.UserId == user.Id).ToListAsync(cancellationToken);

        var submitted = TrainerApplication.Submit(
            user.Id, request.ToDetails(), user.IsTrainer, previous, timeProvider.GetUtcNow().UtcDateTime);

        if (!submitted.IsSuccess)
        {
            return submitted.Error!;
        }

        db.Applications.Add(submitted.Value!);
        await db.SaveChangesAsync(cancellationToken);

        return ApplicationDto.From(submitted.Value!);
    }

    public async Task<Result<ApplicationDto>> Latest(CurrentUser user, CancellationToken cancellationToken)
    {
        var latest = await db.Applications
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.SubmittedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return latest is null ? Error.NotFound("You haven't applied yet.") : ApplicationDto.From(latest);
    }
}

internal static class SubmitApplicationEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/applications", async (TrainerDetailsRequest request, ClaimsPrincipal user, SubmitApplicationHandler handler, CancellationToken ct) =>
                (await handler.Submit(request, CurrentUser.Required(user), ct))
                    .ToHttp(r => TypedResults.Created($"/api/v1/trainers/applications/{r.Id}", r)))
            .WithValidation<TrainerDetailsRequest>()
            .RequireAuthorization()
            .WithName("SubmitTrainerApplication");

        app.MapGet("/applications/mine", async (ClaimsPrincipal user, SubmitApplicationHandler handler, CancellationToken ct) =>
                (await handler.Latest(CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("GetMyTrainerApplication");
    }
}
