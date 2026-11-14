using GymHub.BuildingBlocks.Results;
using GymHub.Modules.TrainingLog.Domain;

namespace GH.UnitTests.TrainingLog;

public class TrainingLogTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private static LoggedExerciseDraft Exercise(string name, params LoggedSet[] sets) => new(name, null, null, sets);

    private static LoggedSet Set(int? reps = null, decimal? weight = null, int? seconds = null) => new(reps, weight, seconds, null, null);

    private static Result<WorkoutSession> Log(DateOnly performedOn, params LoggedExerciseDraft[] exercises) =>
        WorkoutSession.Log("member", performedOn, null, null, null, null, exercises, Now);

    [Theory]
    [InlineData("Back Squat", "back-squat")]
    [InlineData("  back   SQUAT ", "back-squat")]
    [InlineData("Bench Press (DB)", "bench-press-db")]
    [InlineData("Übungs-Satz", "übungs-satz")]
    [InlineData("---", "")]
    [InlineData(null, "")]
    public void Exercise_names_normalize_to_keys(string? name, string key) =>
        Assert.Equal(key, ExerciseKeys.Normalize(name));

    [Theory]
    [InlineData(1, 100, 100)]
    [InlineData(5, 100, 116.7)]
    [InlineData(10, 60, 80)]
    [InlineData(12, 50, 70)]
    public void One_rep_max_is_estimated_with_epley(int reps, decimal weight, decimal expected) =>
        Assert.Equal(expected, SetMath.EstimatedOneRepMax(reps, weight));

    [Theory]
    [InlineData(13, 100)]
    [InlineData(5, 0)]
    [InlineData(null, 100)]
    [InlineData(5, null)]
    public void One_rep_max_needs_a_weighted_set_of_at_most_twelve_reps(int? reps, int? weight) =>
        Assert.Null(SetMath.EstimatedOneRepMax(reps, weight));

    [Fact]
    public void A_session_summarizes_each_exercise()
    {
        var session = Log(Today,
            Exercise("Squat", Set(5, 100), Set(5, 110), Set(15, 60)),
            Exercise("Plank", Set(seconds: 60)),
            Exercise("Push-up", Set(20))).Value!;

        var squat = session.Exercises[0];
        Assert.Equal((110m, 128.3m, 1950m, 25, 3), (squat.TopWeightKg, squat.BestE1RmKg, squat.VolumeKg, squat.TotalReps, squat.SetCount));

        var plank = session.Exercises[1];
        Assert.Equal(((decimal?)null, (decimal?)null, 0m, 0), (plank.TopWeightKg, plank.BestE1RmKg, plank.VolumeKg, plank.TotalReps));

        Assert.Equal((0m, 20), (session.Exercises[2].VolumeKg, session.Exercises[2].TotalReps));
        Assert.Equal(WorkoutSession.DefaultTitle, session.Title);
        Assert.All(session.Exercises, e => Assert.Equal((session.Id, "member", Today), (e.SessionId, e.UserId, e.PerformedOn)));
    }

    [Fact]
    public void Sessions_need_dated_exercises_with_sets_that_say_something()
    {
        Assert.Equal(ErrorKind.Invalid, Log(Today.AddDays(2), Exercise("Squat", Set(5))).Error?.Kind);
        Assert.True(Log(Today.AddDays(1), Exercise("Squat", Set(5))).IsSuccess);
        Assert.Equal(ErrorKind.Invalid, Log(Today).Error?.Kind);
        Assert.Equal(ErrorKind.Invalid, Log(Today, Exercise("Squat")).Error?.Kind);
        Assert.Equal(ErrorKind.Invalid, Log(Today, Exercise("Squat", Set())).Error?.Kind);
        Assert.Equal(ErrorKind.Invalid, Log(Today, Exercise("!!", Set(5))).Error?.Kind);
    }

    [Fact]
    public void Updating_replaces_exercises_and_keeps_the_program_link()
    {
        var link = new ProgramLink(Guid.NewGuid(), "Squat Lab", Guid.NewGuid(), 2, Guid.NewGuid());
        var session = WorkoutSession.Log("member", Today, "Heavy", null, 45, link, [Exercise("Squat", Set(5, 100))], Now).Value!;

        var result = session.Update(Today.AddDays(-1), " ", "Felt good", null, [Exercise("Lunge", Set(10, 20)), Exercise("Squat", Set(3, 120))], Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(["lunge", "squat"], session.Exercises.Select(e => e.ExerciseKey));
        Assert.All(session.Exercises, e => Assert.Equal(Today.AddDays(-1), e.PerformedOn));
        Assert.Equal((WorkoutSession.DefaultTitle, "Felt good", (int?)null), (session.Title, session.Notes, session.DurationMin));
        Assert.Equal((link.ProgramId, link.WorkoutId, 2), (session.ProgramId!.Value, session.ProgramWorkoutId!.Value, session.WeekNumber!.Value));
        Assert.Equal(Now.AddHours(1), session.UpdatedAt);
    }

    [Fact]
    public void Records_are_sessions_that_beat_every_earlier_one()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var entries = new[]
        {
            new RecordEntry(c, Today.AddDays(2), Now, 105, 120),
            new RecordEntry(a, Today, Now, 100, 116.7m),
            new RecordEntry(b, Today.AddDays(1), Now, 110, null),
            new RecordEntry(b, Today.AddDays(1), Now, 90, 130),  // same session, another entry of the exercise
            new RecordEntry(d, Today.AddDays(3), Now, 110, 130)  // ties aren't records
        };

        var history = PersonalRecords.History(entries);

        Assert.Equal(
            [(b, RecordKind.Weight, 110m, 100m), (b, RecordKind.E1Rm, 130m, 116.7m)],
            history.Select(r => (r.SessionId, r.Kind, r.Value, r.Previous)));
    }

    [Fact]
    public void Same_day_sessions_are_ordered_by_when_they_were_logged()
    {
        var later = Guid.NewGuid();
        var earlier = Guid.NewGuid();

        var history = PersonalRecords.History(
        [
            new RecordEntry(later, Today, Now.AddHours(2), 100, null),
            new RecordEntry(earlier, Today, Now, 90, null)
        ]);

        Assert.Equal(later, Assert.Single(history).SessionId);
    }
}
