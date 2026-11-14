using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;

namespace GymHub.Modules.Programs;

internal sealed class ProgramWorkouts(ProgramsDbContext db, TimeProvider timeProvider) : IProgramWorkouts
{
    public async Task<ReleasedWorkout?> GetReleasedWorkoutAsync(Guid programId, Guid workoutId, CancellationToken cancellationToken)
    {
        var program = await LoadAsync(programId, cancellationToken);

        if (program is null)
        {
            return null;
        }

        var match = program.ReleasedWeeks(Today)
            .SelectMany(w => w.Days.SelectMany(d => d.Workouts.Select(workout => (Week: w, Day: d, Workout: workout))))
            .FirstOrDefault(x => x.Workout.Id == workoutId);

        if (match.Workout is null)
        {
            return null;
        }

        return new ReleasedWorkout(
            program.Id,
            program.Title,
            match.Week.Id,
            match.Week.Number,
            match.Day.Number,
            match.Workout.Id,
            match.Workout.Title,
            [.. match.Workout.Exercises.OrderBy(e => e.Order).Select(e => new ReleasedExercise(
                e.Id,
                e.Name,
                [.. e.Sets.Select(s => new PlannedSet(s.Reps, s.WeightKg, s.DurationSec, s.DistanceM, s.RestSec, s.Rpe))]))]);
    }

    public async Task<IReadOnlyList<ReleasedWeek>?> GetReleasedScheduleAsync(Guid programId, CancellationToken cancellationToken)
    {
        var program = await LoadAsync(programId, cancellationToken);

        return program is null
            ? null
            : [.. program.ReleasedWeeks(Today).Select(w => new ReleasedWeek(
                w.Id,
                w.Number,
                w.Title,
                w.ReleasesOn,
                [.. w.Days.OrderBy(d => d.Number)
                    .SelectMany(d => d.Workouts.OrderBy(x => x.Order).Select(x => new ScheduledWorkout(x.Id, d.Number, x.Order, x.Title)))]))];
    }

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private async Task<TrainingProgram?> LoadAsync(Guid programId, CancellationToken cancellationToken)
    {
        var program = await db.LoadProgramAsync(programId, cancellationToken);
        return program is null || program.Status == ProgramStatus.Draft ? null : program;
    }
}
