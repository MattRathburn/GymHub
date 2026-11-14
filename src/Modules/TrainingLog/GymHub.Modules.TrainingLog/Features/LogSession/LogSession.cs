using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.TrainingLog.Data;
using GymHub.Modules.TrainingLog.Domain;
using GymHub.Modules.TrainingLog.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.TrainingLog.Features.LogSession;

/// <summary>
/// Logs a freestyle workout, or one following a released program workout. Program exercises take their names from
/// the program, so they line up with everyone else's logs of the same exercise.
/// </summary>
internal sealed class LogSessionHandler(
    TrainingLogDbContext db,
    ProgramAccess access,
    IProgramWorkouts programWorkouts,
    SessionRecords records,
    TimeProvider timeProvider)
{
    public async Task<Result<SavedSessionResponse>> Handle(SessionRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        ProgramLink? link = null;
        string? defaultTitle = null;
        var programNames = new Dictionary<Guid, string>();

        if (request.ProgramId is { } programId && request.ProgramWorkoutId is { } workoutId)
        {
            var allowed = await access.CheckAsync(programId, user, cancellationToken);

            if (!allowed.IsSuccess)
            {
                return allowed.Error!;
            }

            var workout = await programWorkouts.GetReleasedWorkoutAsync(programId, workoutId, cancellationToken);

            if (workout is null)
            {
                return Error.NotFound("That workout isn't released yet, or is no longer in the program.");
            }

            link = new ProgramLink(workout.ProgramId, workout.ProgramTitle, workout.WeekId, workout.WeekNumber, workout.WorkoutId);
            defaultTitle = workout.Title;
            programNames = workout.Exercises.ToDictionary(e => e.ExerciseId, e => e.Name);
        }

        var drafts = new List<LoggedExerciseDraft>(request.Exercises.Count);

        foreach (var exercise in request.Exercises)
        {
            var name = exercise.Name;

            if (exercise.ProgramExerciseId is { } programExerciseId)
            {
                if (!programNames.TryGetValue(programExerciseId, out var programName))
                {
                    return Error.Invalid($"{exercise.Name.Trim()} isn't part of this program workout.");
                }

                name = programName;
            }

            drafts.Add(new LoggedExerciseDraft(name, exercise.ProgramExerciseId, exercise.Notes, exercise.Sets));
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? defaultTitle : request.Title;
        var session = WorkoutSession.Log(
            user.Id, request.PerformedOn, title, request.Notes, request.DurationMin, link, drafts, timeProvider.GetUtcNow().UtcDateTime);

        if (!session.IsSuccess)
        {
            return session.Error!;
        }

        db.Sessions.Add(session.Value!);
        await db.SaveChangesAsync(cancellationToken);

        return new SavedSessionResponse(session.Value!.Id, await records.ForSessionAsync(session.Value!, cancellationToken));
    }
}

internal static class LogSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/sessions", async (SessionRequest request, ClaimsPrincipal user, LogSessionHandler handler, CancellationToken ct) =>
                (await handler.Handle(request, CurrentUser.Required(user), ct))
                    .ToHttp(r => TypedResults.Created($"/api/v1/training/sessions/{r.Id}", r)))
            .RequireAuthorization()
            .WithValidation<SessionRequest>()
            .WithName("LogSession");
}
