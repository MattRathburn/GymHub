using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;

namespace GymHub.Modules.Programs.Domain;

public sealed class PriceVersion
{
    private PriceVersion() { }

    internal PriceVersion(int version, ProgramPrice price, DateTime effectiveFrom)
    {
        Version = version;
        Price = price;
        EffectiveFrom = effectiveFrom;
    }

    public int Version { get; private set; }
    public ProgramPrice Price { get; private set; } = ProgramPrice.Free;
    public DateTime EffectiveFrom { get; private set; }

    internal void Replace(ProgramPrice price, DateTime effectiveFrom)
    {
        Price = price;
        EffectiveFrom = effectiveFrom;
    }
}

public sealed class ProgramWeek
{
    private readonly List<ProgramDay> _days = [];

    private ProgramWeek() { }

    internal ProgramWeek(int number, string title, DateOnly releasesOn)
    {
        Id = Guid.CreateVersion7();
        Number = number;
        Title = title;
        ReleasesOn = releasesOn;
    }

    public Guid Id { get; private set; }
    public int Number { get; private set; }
    public string Title { get; private set; } = "";

    /// <summary>Calendar date (UTC) on which every subscriber can see this week.</summary>
    public DateOnly ReleasesOn { get; private set; }

    /// <summary>When ProgramWeekReleased was raised, so it is raised once.</summary>
    public DateTime? ReleaseAnnouncedAt { get; private set; }

    public IReadOnlyList<ProgramDay> Days => _days;

    public bool IsReleased(DateOnly today) => ReleasesOn <= today;

    internal void Update(string title, DateOnly releasesOn)
    {
        Title = title;

        if (releasesOn != ReleasesOn)
        {
            ReleasesOn = releasesOn;
            ReleaseAnnouncedAt = null;
        }
    }

    internal void Renumber(int number) => Number = number;

    internal void MarkReleaseAnnounced(DateTime now) => ReleaseAnnouncedAt = now;

    internal ProgramDay? FindDay(int dayNumber) => _days.FirstOrDefault(d => d.Number == dayNumber);

    internal Result<ProgramDay> UpsertDay(int dayNumber, string? title, bool isRestDay, string? notes)
    {
        if (dayNumber is < 1 or > 7)
        {
            return Error.Invalid("Day number must be between 1 and 7.");
        }

        if (FindDay(dayNumber) is { } existing)
        {
            if (isRestDay && existing.Workouts.Count > 0)
            {
                return Error.Conflict("Remove the day's workouts before making it a rest day.");
            }

            existing.Update(title, isRestDay, notes);
            return existing;
        }

        var day = new ProgramDay(dayNumber, title, isRestDay, notes);
        _days.Add(day);
        return day;
    }
}

public sealed class ProgramDay
{
    private readonly List<Workout> _workouts = [];

    private ProgramDay() { }

    internal ProgramDay(int number, string? title, bool isRestDay, string? notes)
    {
        Id = Guid.CreateVersion7();
        Number = number;
        Update(title, isRestDay, notes);
    }

    public Guid Id { get; private set; }
    public int Number { get; private set; }
    public string? Title { get; private set; }
    public bool IsRestDay { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyList<Workout> Workouts => _workouts;

    internal void Update(string? title, bool isRestDay, string? notes)
    {
        Title = title;
        IsRestDay = isRestDay;
        Notes = notes;
    }

    internal Result<Workout> SaveWorkout(Guid? workoutId, string title, string? notes, IReadOnlyList<ExerciseDraft> exercises)
    {
        if (workoutId is null)
        {
            var workout = new Workout(_workouts.Count + 1, title, notes);
            workout.ReplaceExercises(exercises);
            _workouts.Add(workout);
            return workout;
        }

        if (_workouts.FirstOrDefault(w => w.Id == workoutId) is not { } existing)
        {
            return Error.NotFound("Workout not found.");
        }

        existing.Update(title, notes);
        existing.ReplaceExercises(exercises);
        return existing;
    }

    internal bool RemoveWorkout(Guid workoutId)
    {
        var removed = _workouts.RemoveAll(w => w.Id == workoutId) > 0;

        var order = 1;
        foreach (var workout in _workouts.OrderBy(w => w.Order))
        {
            workout.Reorder(order++);
        }

        return removed;
    }
}

/// <summary>Exercise as sent by the editor. Id is set for exercises that already exist, so log history keeps pointing at them.</summary>
public sealed record ExerciseDraft(Guid? Id, string Name, string? Notes, IReadOnlyList<ExerciseSet> Sets);

public sealed class Workout
{
    private readonly List<Exercise> _exercises = [];

    private Workout() { }

    internal Workout(int order, string title, string? notes)
    {
        Id = Guid.CreateVersion7();
        Order = order;
        Title = title;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public int Order { get; private set; }
    public string Title { get; private set; } = "";
    public string? Notes { get; private set; }

    public IReadOnlyList<Exercise> Exercises => _exercises;

    internal void Update(string title, string? notes)
    {
        Title = title;
        Notes = notes;
    }

    internal void Reorder(int order) => Order = order;

    /// <summary>The editor sends the whole list. Exercises keep their ids when the editor sends them back.</summary>
    internal void ReplaceExercises(IReadOnlyList<ExerciseDraft> drafts)
    {
        var keptIds = drafts.Where(d => d.Id is not null).Select(d => d.Id!.Value).ToHashSet();
        _exercises.RemoveAll(e => !keptIds.Contains(e.Id));

        var order = 1;
        foreach (var draft in drafts)
        {
            var existing = draft.Id is null ? null : _exercises.FirstOrDefault(e => e.Id == draft.Id);

            if (existing is null)
            {
                _exercises.Add(new Exercise(order++, draft.Name, draft.Notes, draft.Sets));
            }
            else
            {
                existing.Update(order++, draft.Name, draft.Notes, draft.Sets);
            }
        }
    }
}

public sealed class Exercise
{
    private Exercise() { }

    internal Exercise(int order, string name, string? notes, IReadOnlyList<ExerciseSet> sets)
    {
        Id = Guid.CreateVersion7();
        Update(order, name, notes, sets);
    }

    public Guid Id { get; private set; }
    public int Order { get; private set; }
    public string Name { get; private set; } = "";
    public string? Notes { get; private set; }

    /// <summary>Stored as JSON on the exercise row.</summary>
    public List<ExerciseSet> Sets { get; private set; } = [];

    internal void Update(int order, string name, string? notes, IReadOnlyList<ExerciseSet> sets)
    {
        Order = order;
        Name = name;
        Notes = notes;
        Sets = [.. sets];
    }
}

/// <summary>One prescribed set. Any combination of targets may be given (e.g. reps + weight, or duration + distance).</summary>
public sealed record ExerciseSet(int? Reps, decimal? WeightKg, int? DurationSec, int? DistanceM, int? RestSec, decimal? Rpe);

public enum MediaTarget
{
    Cover,
    Program,
    Week,
    Day,
    Workout,
    Exercise
}

public enum MediaKind
{
    Image,
    Video
}

public sealed class MediaAttachment
{
    private MediaAttachment() { }

    internal MediaAttachment(MediaTarget targetKind, Guid targetId, string objectKey, string contentType, MediaKind kind, string? caption)
    {
        Id = Guid.CreateVersion7();
        TargetKind = targetKind;
        TargetId = targetId;
        ObjectKey = objectKey;
        ContentType = contentType;
        Kind = kind;
        Caption = caption;
    }

    public Guid Id { get; private set; }
    public MediaTarget TargetKind { get; private set; }
    public Guid TargetId { get; private set; }
    public string ObjectKey { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public MediaKind Kind { get; private set; }
    public string? Caption { get; private set; }
}
