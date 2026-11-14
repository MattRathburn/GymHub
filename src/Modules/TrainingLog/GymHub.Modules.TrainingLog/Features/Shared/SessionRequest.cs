using FluentValidation;
using GymHub.Modules.TrainingLog.Domain;

namespace GymHub.Modules.TrainingLog.Features.Shared;

public sealed record LoggedExerciseRequest(string Name, Guid? ProgramExerciseId, string? Notes, IReadOnlyList<LoggedSet> Sets);

/// <summary>
/// What the log form sends. ProgramId and ProgramWorkoutId are read only when logging a new session: a session keeps
/// the program workout it was logged against.
/// </summary>
public sealed record SessionRequest(
    DateOnly PerformedOn,
    string? Title,
    string? Notes,
    int? DurationMin,
    Guid? ProgramId,
    Guid? ProgramWorkoutId,
    IReadOnlyList<LoggedExerciseRequest> Exercises);

internal sealed class SessionRequestValidator : AbstractValidator<SessionRequest>
{
    public SessionRequestValidator()
    {
        RuleFor(r => r.PerformedOn).NotEmpty();
        RuleFor(r => r.Title).MaximumLength(WorkoutSession.MaxTitleLength);
        RuleFor(r => r.Notes).MaximumLength(5_000);
        RuleFor(r => r.DurationMin).InclusiveBetween(1, 24 * 60).When(r => r.DurationMin is not null);
        RuleFor(r => r.ProgramWorkoutId).NotNull().When(r => r.ProgramId is not null).WithMessage("Choose the program workout you did.");
        RuleFor(r => r.ProgramId).NotNull().When(r => r.ProgramWorkoutId is not null).WithMessage("Choose the program the workout belongs to.");
        RuleFor(r => r.Exercises).NotNull()
            .Must(e => e is { Count: > 0 and <= WorkoutSession.MaxExercises })
            .WithMessage($"Log between 1 and {WorkoutSession.MaxExercises} exercises.");

        RuleForEach(r => r.Exercises).ChildRules(exercise =>
        {
            exercise.RuleFor(e => e.Name).NotEmpty().MaximumLength(200)
                .Must(n => ExerciseKeys.Normalize(n).Length > 0).WithMessage("An exercise name needs a letter or a digit.");
            exercise.RuleFor(e => e.Notes).MaximumLength(2_000);
            exercise.RuleFor(e => e.Sets).NotNull()
                .Must(s => s is { Count: > 0 and <= WorkoutSession.MaxSets })
                .WithMessage($"Log between 1 and {WorkoutSession.MaxSets} sets per exercise.");
            exercise.RuleForEach(e => e.Sets).ChildRules(set =>
            {
                set.RuleFor(s => s).Must(s => s.HasAnyValue).WithMessage("A set needs reps, weight, time or distance.");
                set.RuleFor(s => s.Reps).InclusiveBetween(1, 1_000).When(s => s.Reps is not null);
                set.RuleFor(s => s.WeightKg).InclusiveBetween(0, 1_000).When(s => s.WeightKg is not null);
                set.RuleFor(s => s.DurationSec).InclusiveBetween(1, 24 * 60 * 60).When(s => s.DurationSec is not null);
                set.RuleFor(s => s.DistanceM).InclusiveBetween(1, 1_000_000).When(s => s.DistanceM is not null);
                set.RuleFor(s => s.Rpe).InclusiveBetween(1, 10).When(s => s.Rpe is not null);
            });
        });
    }
}

/// <summary>The id of a saved session, and the personal records it set.</summary>
public sealed record SavedSessionResponse(Guid Id, IReadOnlyList<RecordDto> Records);

public sealed record RecordDto(string ExerciseKey, string Name, RecordKind Kind, decimal Value, decimal Previous);
