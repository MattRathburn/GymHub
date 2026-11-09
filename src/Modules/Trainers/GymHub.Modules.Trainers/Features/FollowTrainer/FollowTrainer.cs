using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Trainers.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Features.FollowTrainer;

/// <summary>Following and unfollowing a trainer. Both are idempotent.</summary>
internal sealed class FollowTrainerHandler(TrainersDbContext db, TimeProvider timeProvider)
{
    public async Task<Result> Follow(string trainerId, CurrentUser user, CancellationToken cancellationToken)
    {
        if (!await db.Profiles.AnyAsync(p => p.UserId == trainerId, cancellationToken))
        {
            return Error.NotFound("Trainer not found.");
        }

        if (await db.Follows.AnyAsync(f => f.UserId == user.Id && f.TrainerId == trainerId, cancellationToken))
        {
            return Result.Success();
        }

        var follow = Domain.Follow.Start(user.Id, trainerId, timeProvider.GetUtcNow().UtcDateTime);

        if (!follow.IsSuccess)
        {
            return follow.Error!;
        }

        db.Follows.Add(follow.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Unfollow(string trainerId, CurrentUser user, CancellationToken cancellationToken)
    {
        await db.Follows.Where(f => f.UserId == user.Id && f.TrainerId == trainerId).ExecuteDeleteAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class FollowTrainerEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/{trainerId}/follow", async (string trainerId, ClaimsPrincipal user, FollowTrainerHandler handler, CancellationToken ct) =>
                (await handler.Follow(trainerId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("FollowTrainer");

        app.MapDelete("/{trainerId}/follow", async (string trainerId, ClaimsPrincipal user, FollowTrainerHandler handler, CancellationToken ct) =>
                (await handler.Unfollow(trainerId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("UnfollowTrainer");
    }
}
