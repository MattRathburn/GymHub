using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Features.ListFollowing;

public sealed record FollowedTrainer(string UserId, string DisplayName, Uri? AvatarUrl, IReadOnlyList<string> Specialties, DateTime FollowedAt);

internal sealed class ListFollowingHandler(TrainersDbContext db, IObjectStorage storage)
{
    public async Task<IReadOnlyList<FollowedTrainer>> Handle(CurrentUser user, CancellationToken cancellationToken)
    {
        var rows = await db.Follows
            .AsNoTracking()
            .Where(f => f.UserId == user.Id)
            .Join(db.Profiles, f => f.TrainerId, p => p.UserId, (f, p) => new { f.FollowedAt, Profile = p })
            .OrderByDescending(x => x.FollowedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new FollowedTrainer(
            x.Profile.UserId, x.Profile.DisplayName, storage.AvatarUrl(x.Profile.AvatarObjectKey), x.Profile.Specialties, x.FollowedAt))];
    }
}

internal static class ListFollowingEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/following", async (ClaimsPrincipal user, ListFollowingHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(CurrentUser.Required(user), ct)))
            .RequireAuthorization()
            .WithName("ListFollowedTrainers");
}
