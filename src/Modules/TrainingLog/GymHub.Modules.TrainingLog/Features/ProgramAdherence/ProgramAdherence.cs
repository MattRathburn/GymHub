using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.TrainingLog.Data;
using GymHub.Modules.TrainingLog.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.TrainingLog.Features.ProgramAdherence;

public sealed record AdherenceWorkoutDto(Guid WorkoutId, int DayNumber, string Title, Guid? LastSessionId, DateOnly? LastPerformedOn);

public sealed record AdherenceWeekDto(Guid WeekId, int WeekNumber, string Title, DateOnly ReleasesOn, int Planned, int Completed, IReadOnlyList<AdherenceWorkoutDto> Workouts);

public sealed record RemovedWorkoutSessionDto(Guid SessionId, DateOnly PerformedOn, string Title, int? WeekNumber);

/// <summary>Released program workouts against the member's sessions. Sessions whose workout was later removed are listed apart.</summary>
public sealed record AdherenceDto(
    Guid ProgramId,
    int Planned,
    int Completed,
    IReadOnlyList<AdherenceWeekDto> Weeks,
    IReadOnlyList<RemovedWorkoutSessionDto> NoLongerInProgram);

internal sealed class ProgramAdherenceHandler(TrainingLogDbContext db, ProgramAccess access, IProgramWorkouts programWorkouts)
{
    public async Task<Result<AdherenceDto>> Handle(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var allowed = await access.CheckAsync(programId, user, cancellationToken);

        if (!allowed.IsSuccess)
        {
            return allowed.Error!;
        }

        var schedule = await programWorkouts.GetReleasedScheduleAsync(programId, cancellationToken);

        if (schedule is null)
        {
            return Error.NotFound("Program not found.");
        }

        var sessions = await db.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id && s.ProgramId == programId && s.ProgramWorkoutId != null)
            .Select(s => new { s.Id, s.PerformedOn, s.CreatedAt, s.Title, s.WeekNumber, WorkoutId = s.ProgramWorkoutId!.Value })
            .ToListAsync(cancellationToken);

        var latestByWorkout = sessions
            .GroupBy(s => s.WorkoutId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => (s.PerformedOn, s.CreatedAt))!);

        var weeks = schedule
            .Select(week =>
            {
                var workouts = week.Workouts
                    .Select(w =>
                    {
                        var last = latestByWorkout.GetValueOrDefault(w.WorkoutId);
                        return new AdherenceWorkoutDto(w.WorkoutId, w.DayNumber, w.Title, last?.Id, last?.PerformedOn);
                    })
                    .ToList();

                return new AdherenceWeekDto(
                    week.WeekId, week.WeekNumber, week.Title, week.ReleasesOn,
                    workouts.Count, workouts.Count(w => w.LastSessionId is not null), workouts);
            })
            .ToList();

        var scheduled = schedule.SelectMany(w => w.Workouts).Select(w => w.WorkoutId).ToHashSet();
        var removed = sessions
            .Where(s => !scheduled.Contains(s.WorkoutId))
            .OrderByDescending(s => s.PerformedOn)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => new RemovedWorkoutSessionDto(s.Id, s.PerformedOn, s.Title, s.WeekNumber))
            .ToList();

        return new AdherenceDto(programId, weeks.Sum(w => w.Planned), weeks.Sum(w => w.Completed), weeks, removed);
    }
}

internal static class ProgramAdherenceEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/programs/{programId:guid}/adherence", async (Guid programId, ClaimsPrincipal user, ProgramAdherenceHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("GetProgramAdherence");
}
