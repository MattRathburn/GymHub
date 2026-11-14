using GymHub.BuildingBlocks.Results;

namespace GymHub.Modules.TrainingLog.Domain;

/// <summary>One performed set. Any combination of values may be given (e.g. reps + weight, or duration + distance).</summary>
public sealed record LoggedSet(int? Reps, decimal? WeightKg, int? DurationSec, int? DistanceM, decimal? Rpe)
{
    public bool HasAnyValue => Reps is not null || WeightKg is not null || DurationSec is not null || DistanceM is not null;
}

/// <summary>An exercise as sent by the log form. ProgramExerciseId links it to the program exercise it followed.</summary>
public sealed record LoggedExerciseDraft(string Name, Guid? ProgramExerciseId, string? Notes, IReadOnlyList<LoggedSet> Sets);

/// <summary>Snapshot of the program workout a session followed. The ids are references only; the program may change later.</summary>
public sealed record ProgramLink(Guid ProgramId, string ProgramTitle, Guid WeekId, int WeekNumber, Guid WorkoutId);

/// <summary>A workout the member did: freestyle, or following a released program workout.</summary>
public sealed class WorkoutSession
{
    public const int MaxExercises = 30;
    public const int MaxSets = 50;
    public const int MaxTitleLength = 200;
    public const string DefaultTitle = "Workout";

    private static readonly DateOnly EarliestDate = new(2000, 1, 1);

    private readonly List<LoggedExercise> _exercises = [];

    private WorkoutSession() { }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = "";
    public DateOnly PerformedOn { get; private set; }
    public string Title { get; private set; } = "";
    public string? Notes { get; private set; }
    public int? DurationMin { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Guid? ProgramId { get; private set; }
    public string? ProgramTitle { get; private set; }
    public Guid? WeekId { get; private set; }
    public int? WeekNumber { get; private set; }
    public Guid? ProgramWorkoutId { get; private set; }

    public IReadOnlyList<LoggedExercise> Exercises => _exercises;

    public static Result<WorkoutSession> Log(
        string userId,
        DateOnly performedOn,
        string? title,
        string? notes,
        int? durationMin,
        ProgramLink? program,
        IReadOnlyList<LoggedExerciseDraft> exercises,
        DateTime now)
    {
        var session = new WorkoutSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAt = now,
            ProgramId = program?.ProgramId,
            ProgramTitle = program?.ProgramTitle,
            WeekId = program?.WeekId,
            WeekNumber = program?.WeekNumber,
            ProgramWorkoutId = program?.WorkoutId
        };

        var result = session.Apply(performedOn, title, notes, durationMin, exercises, now);
        return result.IsSuccess ? session : result.Error!;
    }

    /// <summary>Replaces what was logged. The program link stays as it was.</summary>
    public Result Update(DateOnly performedOn, string? title, string? notes, int? durationMin, IReadOnlyList<LoggedExerciseDraft> exercises, DateTime now) =>
        Apply(performedOn, title, notes, durationMin, exercises, now);

    public bool IsOwnedBy(string userId) => UserId == userId;

    private Result Apply(DateOnly performedOn, string? title, string? notes, int? durationMin, IReadOnlyList<LoggedExerciseDraft> exercises, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);

        // A day of slack, since the member's local date can be ahead of UTC.
        if (performedOn > today.AddDays(1) || performedOn < EarliestDate)
        {
            return Error.Invalid("The workout date can't be in the future.");
        }

        if (exercises.Count is 0 or > MaxExercises)
        {
            return Error.Invalid($"Log between 1 and {MaxExercises} exercises.");
        }

        foreach (var exercise in exercises)
        {
            if (ExerciseKeys.Normalize(exercise.Name).Length == 0)
            {
                return Error.Invalid("Every exercise needs a name.");
            }

            if (exercise.Sets.Count is 0 or > MaxSets)
            {
                return Error.Invalid($"Log between 1 and {MaxSets} sets for {exercise.Name.Trim()}.");
            }

            if (exercise.Sets.Any(s => !s.HasAnyValue))
            {
                return Error.Invalid($"Every set of {exercise.Name.Trim()} needs reps, weight, time or distance.");
            }
        }

        PerformedOn = performedOn;
        Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        DurationMin = durationMin;
        UpdatedAt = now;

        _exercises.Clear();
        var order = 1;
        foreach (var draft in exercises)
        {
            _exercises.Add(new LoggedExercise(this, order++, draft));
        }

        return Result.Success();
    }
}

/// <summary>
/// One exercise of a session. Owner, date and summary numbers are copied onto the row so progress and records are
/// plain SQL over columns, not JSON.
/// </summary>
public sealed class LoggedExercise
{
    private LoggedExercise() { }

    internal LoggedExercise(WorkoutSession session, int order, LoggedExerciseDraft draft)
    {
        Id = Guid.CreateVersion7();
        SessionId = session.Id;
        UserId = session.UserId;
        PerformedOn = session.PerformedOn;
        LoggedAt = session.CreatedAt;
        Order = order;
        Name = draft.Name.Trim();
        ExerciseKey = ExerciseKeys.Normalize(draft.Name);
        ProgramExerciseId = draft.ProgramExerciseId;
        Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();
        Sets = [.. draft.Sets];

        var summary = SetMath.Summarize(Sets);
        TopWeightKg = summary.TopWeightKg;
        BestE1RmKg = summary.BestE1RmKg;
        VolumeKg = summary.VolumeKg;
        TotalReps = summary.TotalReps;
        SetCount = Sets.Count;
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public string UserId { get; private set; } = "";
    public DateOnly PerformedOn { get; private set; }

    /// <summary>When the session was first logged; orders sessions on the same date.</summary>
    public DateTime LoggedAt { get; private set; }

    public int Order { get; private set; }
    public string Name { get; private set; } = "";
    public string ExerciseKey { get; private set; } = "";
    public Guid? ProgramExerciseId { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Stored as JSON on the row.</summary>
    public List<LoggedSet> Sets { get; private set; } = [];

    public decimal? TopWeightKg { get; private set; }
    public decimal? BestE1RmKg { get; private set; }
    public decimal VolumeKg { get; private set; }
    public int TotalReps { get; private set; }
    public int SetCount { get; private set; }
}
