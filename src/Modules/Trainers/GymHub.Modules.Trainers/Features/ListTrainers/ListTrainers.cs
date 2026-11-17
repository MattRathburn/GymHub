using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Persistence;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Reviews.Contracts;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Features.ListTrainers;

/// <summary>Average is null until the trainer's programs have reviews.</summary>
public sealed record TrainerRatingDto(decimal? Average, int Count);

public sealed record TrainerCard(
    string UserId, string DisplayName, string? Bio, IReadOnlyList<string> Specialties, Uri? AvatarUrl,
    int FollowerCount, int ProgramCount, TrainerRatingDto Rating);

/// <summary>
/// The public trainer directory: trainers with at least one published program, most followed first. Search matches
/// the name, bio or a specialty.
/// </summary>
internal sealed class ListTrainersHandler(TrainersDbContext db, IProgramCatalog catalog, IObjectStorage storage)
{
    public async Task<PagedResult<TrainerCard>> Handle(string? search, PaginationRequest page, CancellationToken cancellationToken)
    {
        var trainerIds = await catalog.GetPublishingTrainerIdsAsync(cancellationToken);

        var profiles = db.Profiles.AsNoTracking().Where(p => trainerIds.Contains(p.UserId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern.Contains(search);
            profiles = profiles.Where(p =>
                EF.Functions.ILike(p.DisplayName, pattern, LikePattern.EscapeCharacter)
                || (p.Bio != null && EF.Functions.ILike(p.Bio, pattern, LikePattern.EscapeCharacter))
                || p.Specialties.Any(s => EF.Functions.ILike(s, pattern, LikePattern.EscapeCharacter)));
        }

        var count = await profiles.LongCountAsync(cancellationToken);

        var rows = await profiles
            .Select(p => new { Profile = p, Followers = db.Follows.Count(f => f.TrainerId == p.UserId) })
            .OrderByDescending(x => x.Followers)
            .ThenBy(x => x.Profile.DisplayName)
            .Skip(page.Skip)
            .Take(page.Take)
            .ToListAsync(cancellationToken);

        // Totals only for the trainers on this page. A program archived since the first query leaves its trainer at zero.
        var statsById = await catalog.GetTrainerStatsAsync([.. rows.Select(x => x.Profile.UserId)], cancellationToken);
        var empty = new TrainerCatalogStats(0, 0, 0);

        var cards = rows.Select(x =>
        {
            var stats = statsById.GetValueOrDefault(x.Profile.UserId, empty);
            return new TrainerCard(
                x.Profile.UserId, x.Profile.DisplayName, x.Profile.Bio, x.Profile.Specialties, storage.AvatarUrl(x.Profile.AvatarObjectKey),
                x.Followers, stats.ProgramCount, new TrainerRatingDto(Ratings.Average(stats.RatingCount, stats.RatingSum), stats.RatingCount));
        }).ToList();

        return new PagedResult<TrainerCard>(page.PageIndex, page.Take, count, cards);
    }
}

internal static class ListTrainersEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (string? search, [AsParameters] PaginationRequest page, ListTrainersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(search, page, ct)))
            .AllowAnonymous()
            .WithName("ListTrainers");
}
