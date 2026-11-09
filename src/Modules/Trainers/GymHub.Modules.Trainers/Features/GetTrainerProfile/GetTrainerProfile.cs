using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Features.GetTrainerProfile;

/// <summary>A trainer's public page. The trainer's programs come from GET /programs?trainerId=.</summary>
public sealed record TrainerProfileDto(
    string UserId, string DisplayName, string? Bio, IReadOnlyList<string> Specialties, IReadOnlyList<string> Links,
    Uri? AvatarUrl, int FollowerCount, bool IsFollowing);

internal sealed class GetTrainerProfileHandler(TrainersDbContext db, IObjectStorage storage)
{
    public async Task<Result<TrainerProfileDto>> Handle(string trainerId, CurrentUser? viewer, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == trainerId, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound("Trainer not found.");
        }

        var followers = await db.Follows.CountAsync(f => f.TrainerId == trainerId, cancellationToken);
        var isFollowing = viewer is not null
            && await db.Follows.AnyAsync(f => f.TrainerId == trainerId && f.UserId == viewer.Id, cancellationToken);

        return new TrainerProfileDto(
            profile.UserId, profile.DisplayName, profile.Bio, profile.Specialties, profile.Links,
            storage.AvatarUrl(profile.AvatarObjectKey), followers, isFollowing);
    }
}

internal static class GetTrainerProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{trainerId}", async (string trainerId, ClaimsPrincipal user, GetTrainerProfileHandler handler, CancellationToken ct) =>
                (await handler.Handle(trainerId, CurrentUser.From(user), ct)).ToHttp())
            .AllowAnonymous()
            .WithName("GetTrainerProfile");
}
