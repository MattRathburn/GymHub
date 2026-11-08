using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.ManageWorkouts;

public sealed record DayRequest(string? Title, bool IsRestDay, string? Notes);

public sealed record DayResponse(Guid DayId);

public sealed record ExerciseRequest(Guid? Id, string Name, string? Notes, IReadOnlyList<ExerciseSet> Sets);

public sealed record WorkoutRequest(string Title, string? Notes, IReadOnlyList<ExerciseRequest> Exercises);

public sealed record WorkoutResponse(Guid WorkoutId, IReadOnlyList<Guid> ExerciseIds);

internal sealed class DayRequestValidator : AbstractValidator<DayRequest>
{
    public DayRequestValidator()
    {
        RuleFor(r => r.Title).MaximumLength(200);
        RuleFor(r => r.Notes).MaximumLength(5_000);
    }
}

internal sealed class WorkoutRequestValidator : AbstractValidator<WorkoutRequest>
{
    public WorkoutRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Notes).MaximumLength(5_000);
        RuleFor(r => r.Exercises).NotNull().Must(e => e.Count <= 50).WithMessage("A workout can have at most 50 exercises.");
        RuleForEach(r => r.Exercises).ChildRules(exercise =>
        {
            exercise.RuleFor(e => e.Name).NotEmpty().MaximumLength(200);
            exercise.RuleFor(e => e.Notes).MaximumLength(2_000);
            exercise.RuleFor(e => e.Sets).NotNull().Must(s => s.Count <= 30).WithMessage("An exercise can have at most 30 sets.");
            exercise.RuleForEach(e => e.Sets).ChildRules(set =>
            {
                set.RuleFor(s => s.Reps).GreaterThan(0).When(s => s.Reps is not null);
                set.RuleFor(s => s.WeightKg).GreaterThanOrEqualTo(0).When(s => s.WeightKg is not null);
                set.RuleFor(s => s.DurationSec).GreaterThan(0).When(s => s.DurationSec is not null);
                set.RuleFor(s => s.DistanceM).GreaterThan(0).When(s => s.DistanceM is not null);
                set.RuleFor(s => s.RestSec).GreaterThanOrEqualTo(0).When(s => s.RestSec is not null);
                set.RuleFor(s => s.Rpe).InclusiveBetween(1, 10).When(s => s.Rpe is not null);
            });
        });
    }
}

internal sealed class ManageWorkoutsHandler(ProgramsDbContext db)
{
    public async Task<Result<DayResponse>> UpsertDay(Guid programId, Guid weekId, int dayNumber, DayRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var day = loaded.Value!.UpsertDay(weekId, dayNumber, request.Title, request.IsRestDay, request.Notes);

        if (!day.IsSuccess)
        {
            return day.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new DayResponse(day.Value!.Id);
    }

    public async Task<Result<WorkoutResponse>> Save(Guid programId, Guid weekId, int dayNumber, Guid? workoutId, WorkoutRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var drafts = request.Exercises.Select(e => new ExerciseDraft(e.Id, e.Name, e.Notes, e.Sets)).ToList();
        var saved = loaded.Value!.SaveWorkout(weekId, dayNumber, workoutId, request.Title, request.Notes, drafts);

        if (!saved.IsSuccess)
        {
            return saved.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);

        var workout = saved.Value!;
        return new WorkoutResponse(workout.Id, [.. workout.Exercises.OrderBy(e => e.Order).Select(e => e.Id)]);
    }

    public async Task<Result> Remove(Guid programId, Guid weekId, int dayNumber, Guid workoutId, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var result = loaded.Value!.RemoveWorkout(weekId, dayNumber, workoutId);

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

internal static class ManageWorkoutsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var day = app.MapGroup("/{programId:guid}/weeks/{weekId:guid}/days/{dayNumber:int}").RequireAuthorization(Policies.Trainer);

        day.MapPut("/", async (Guid programId, Guid weekId, int dayNumber, DayRequest request, ClaimsPrincipal user, ManageWorkoutsHandler handler, CancellationToken ct) =>
                (await handler.UpsertDay(programId, weekId, dayNumber, request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<DayRequest>()
            .WithName("UpsertDay");

        day.MapPost("/workouts", async (Guid programId, Guid weekId, int dayNumber, WorkoutRequest request, ClaimsPrincipal user, ManageWorkoutsHandler handler, CancellationToken ct) =>
                (await handler.Save(programId, weekId, dayNumber, null, request, CurrentUser.Required(user), ct))
                    .ToHttp(r => TypedResults.Created($"/api/v1/programs/{programId}/weeks/{weekId}/days/{dayNumber}/workouts/{r.WorkoutId}", r)))
            .WithValidation<WorkoutRequest>()
            .WithName("AddWorkout");

        day.MapPut("/workouts/{workoutId:guid}", async (Guid programId, Guid weekId, int dayNumber, Guid workoutId, WorkoutRequest request, ClaimsPrincipal user, ManageWorkoutsHandler handler, CancellationToken ct) =>
                (await handler.Save(programId, weekId, dayNumber, workoutId, request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<WorkoutRequest>()
            .WithName("UpdateWorkout");

        day.MapDelete("/workouts/{workoutId:guid}", async (Guid programId, Guid weekId, int dayNumber, Guid workoutId, ClaimsPrincipal user, ManageWorkoutsHandler handler, CancellationToken ct) =>
                (await handler.Remove(programId, weekId, dayNumber, workoutId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("RemoveWorkout");
    }
}
