using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.TrainingLog.Data;
using GymHub.Modules.TrainingLog.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.TrainingLog.Features.Exercises;

public sealed record ExerciseSummaryDto(string Key, string Name, DateOnly LastPerformedOn, int SessionCount, decimal? BestWeightKg, decimal? BestE1RmKg);

/// <summary>An exercise's numbers in one session.</summary>
public sealed record ProgressPointDto(Guid SessionId, DateOnly PerformedOn, decimal? TopWeightKg, decimal? BestE1RmKg, decimal VolumeKg, int TotalReps);

public sealed record RecordHistoryDto(Guid SessionId, DateOnly PerformedOn, RecordKind Kind, decimal Value, decimal Previous);

public sealed record ExerciseProgressDto(
    string Key,
    string Name,
    decimal? BestWeightKg,
    decimal? BestE1RmKg,
    IReadOnlyList<ProgressPointDto> Points,
    IReadOnlyList<RecordHistoryDto> Records);

/// <summary>Every exercise the member has logged, and one exercise's progress and personal records over time.</summary>
internal sealed class ExercisesHandler(TrainingLogDbContext db)
{
    public async Task<IReadOnlyList<ExerciseSummaryDto>> List(CurrentUser user, CancellationToken cancellationToken)
    {
        var rows = await Rows(user).ToListAsync(cancellationToken);

        return
        [
            .. rows
                .GroupBy(r => r.ExerciseKey)
                .Select(g =>
                {
                    var latest = g.MaxBy(r => (r.PerformedOn, r.LoggedAt))!;
                    return new ExerciseSummaryDto(
                        g.Key,
                        latest.Name,
                        latest.PerformedOn,
                        g.Select(r => r.SessionId).Distinct().Count(),
                        g.Max(r => r.TopWeightKg),
                        g.Max(r => r.BestE1RmKg));
                })
                .OrderByDescending(e => e.LastPerformedOn)
                .ThenBy(e => e.Name)
        ];
    }

    public async Task<Result<ExerciseProgressDto>> Progress(string key, CurrentUser user, CancellationToken cancellationToken)
    {
        var rows = await Rows(user, key).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return Error.NotFound("You haven't logged this exercise yet.");
        }

        var points = rows
            .GroupBy(r => r.SessionId)
            .Select(g => new
            {
                g.First().PerformedOn,
                g.First().LoggedAt,
                Point = new ProgressPointDto(
                    g.Key,
                    g.First().PerformedOn,
                    g.Max(r => r.TopWeightKg),
                    g.Max(r => r.BestE1RmKg),
                    g.Sum(r => r.VolumeKg),
                    g.Sum(r => r.TotalReps))
            })
            .OrderBy(p => p.PerformedOn)
            .ThenBy(p => p.LoggedAt)
            .Select(p => p.Point)
            .ToList();

        var records = PersonalRecords
            .History(rows.Select(r => new RecordEntry(r.SessionId, r.PerformedOn, r.LoggedAt, r.TopWeightKg, r.BestE1RmKg)))
            .Select(r => new RecordHistoryDto(r.SessionId, r.PerformedOn, r.Kind, r.Value, r.Previous))
            .ToList();

        return new ExerciseProgressDto(
            key,
            rows.MaxBy(r => (r.PerformedOn, r.LoggedAt))!.Name,
            rows.Max(r => r.TopWeightKg),
            rows.Max(r => r.BestE1RmKg),
            points,
            records);
    }

    private IQueryable<ExerciseRow> Rows(CurrentUser user, string? key = null) =>
        db.Exercises
            .AsNoTracking()
            .Where(e => e.UserId == user.Id && (key == null || e.ExerciseKey == key))
            .Select(e => new ExerciseRow(e.ExerciseKey, e.Name, e.SessionId, e.PerformedOn, e.LoggedAt, e.TopWeightKg, e.BestE1RmKg, e.VolumeKg, e.TotalReps));

    private sealed record ExerciseRow(
        string ExerciseKey,
        string Name,
        Guid SessionId,
        DateOnly PerformedOn,
        DateTime LoggedAt,
        decimal? TopWeightKg,
        decimal? BestE1RmKg,
        decimal VolumeKg,
        int TotalReps);
}

internal static class ExercisesEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/exercises", async (ClaimsPrincipal user, ExercisesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.List(CurrentUser.Required(user), ct)))
            .RequireAuthorization()
            .WithName("ListLoggedExercises");

        app.MapGet("/exercises/{key}/progress", async (string key, ClaimsPrincipal user, ExercisesHandler handler, CancellationToken ct) =>
                (await handler.Progress(ExerciseKeys.Normalize(key), CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("GetExerciseProgress");
    }
}
