using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Paging;

namespace GH.FunctionalTests;

public class TrainingLogApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _trainerId = $"trainer-{Guid.NewGuid():N}";
    private readonly string _memberId = $"member-{Guid.NewGuid():N}";

    private HttpClient Trainer => factory.ClientFor(_trainerId, "trainer");
    private HttpClient Member => factory.ClientFor(_memberId);

    private sealed record IdResponse(Guid Id);
    private sealed record AddWeekResponse(Guid WeekId, int Number);
    private sealed record WorkoutResponse(Guid WorkoutId, IReadOnlyList<Guid> ExerciseIds);
    private sealed record Record(string ExerciseKey, string Name, string Kind, decimal Value, decimal Previous);
    private sealed record Saved(Guid Id, IReadOnlyList<Record> Records);
    private sealed record Summary(Guid Id, DateOnly PerformedOn, string Title, Guid? ProgramId, int ExerciseCount, int SetCount, decimal VolumeKg, IReadOnlyList<string> ExerciseNames);
    private sealed record LoggedExercise(string Name, string ExerciseKey, Guid? ProgramExerciseId, decimal? TopWeightKg, decimal? BestE1RmKg);
    private sealed record Session(Guid Id, string Title, Guid? ProgramId, string? ProgramTitle, int? WeekNumber, Guid? ProgramWorkoutId, IReadOnlyList<LoggedExercise> Exercises);
    private sealed record ExerciseSummary(string Key, string Name, int SessionCount, decimal? BestWeightKg, decimal? BestE1RmKg);
    private sealed record Point(Guid SessionId, decimal? TopWeightKg, decimal? BestE1RmKg, decimal VolumeKg);
    private sealed record HistoryRecord(Guid SessionId, string Kind, decimal Value, decimal Previous);
    private sealed record Progress(string Key, string Name, decimal? BestWeightKg, IReadOnlyList<Point> Points, IReadOnlyList<HistoryRecord> Records);
    private sealed record AdherenceWorkout(Guid WorkoutId, string Title, Guid? LastSessionId);
    private sealed record AdherenceWeek(int WeekNumber, int Planned, int Completed, IReadOnlyList<AdherenceWorkout> Workouts);
    private sealed record Removed(Guid SessionId);
    private sealed record Adherence(int Planned, int Completed, IReadOnlyList<AdherenceWeek> Weeks, IReadOnlyList<Removed> NoLongerInProgram);

    private sealed record ProgramSetup(Guid ProgramId, Guid WeekId, Guid WorkoutId, Guid SquatId, Guid FutureWorkoutId);

    /// <summary>
    /// A published free program: week 1 (released) with a squat workout, and week 2 (next week). Optionally with the
    /// member enrolled.
    /// </summary>
    private async Task<ProgramSetup> ProgramAsync(bool enrollMember = true)
    {
        var created = await Trainer.PostAsJsonAsync("/api/v1/programs/", new { title = $"Squat Lab {Guid.NewGuid():N}", kind = "Ongoing" }, Json, Ct);
        var id = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;

        var week1 = await AddWeekAsync(id, "Week 1", Today);
        var week2 = await AddWeekAsync(id, "Week 2", Today.AddDays(7));
        var workout = await AddWorkoutAsync(id, week1, "Squat day", "Back Squat");
        var future = await AddWorkoutAsync(id, week2, "Heavy day", "Back Squat");

        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/programs/{id}/publish", null, Ct)).StatusCode);

        if (enrollMember)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await Member.PutAsync($"/api/v1/subscriptions/programs/{id}", null, Ct)).StatusCode);
        }

        return new ProgramSetup(id, week1, workout.WorkoutId, workout.ExerciseIds[0], future.WorkoutId);
    }

    private async Task<Guid> AddWeekAsync(Guid programId, string title, DateOnly releasesOn)
    {
        var response = await Trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/weeks", new { title, releasesOn }, Json, Ct);
        return (await response.Content.ReadFromJsonAsync<AddWeekResponse>(Json, Ct))!.WeekId;
    }

    private async Task<WorkoutResponse> AddWorkoutAsync(Guid programId, Guid weekId, string title, string exercise)
    {
        var day = await Trainer.PutAsJsonAsync($"/api/v1/programs/{programId}/weeks/{weekId}/days/1", new { isRestDay = false }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, day.StatusCode);

        var response = await Trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/weeks/{weekId}/days/1/workouts", new
        {
            title,
            exercises = new[] { new { name = exercise, sets = new[] { new { reps = 5, weightKg = 100 } } } }
        }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkoutResponse>(Json, Ct))!;
    }

    private static object Squat(decimal weight, int reps = 5, string name = "back squat", Guid? programExerciseId = null) =>
        new { name, programExerciseId, sets = new[] { new { reps, weightKg = weight }, new { reps, weightKg = weight - 10 } } };

    private static object SessionBody(DateOnly performedOn, object[] exercises, Guid? programId = null, Guid? programWorkoutId = null, string? title = null) =>
        new { performedOn, title, programId, programWorkoutId, exercises };

    private static async Task<Saved> LogAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/training/sessions", body, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Saved>(Json, Ct))!;
    }

    [Fact]
    public async Task A_freestyle_session_round_trips_and_only_its_owner_can_see_it()
    {
        var saved = await LogAsync(Member, SessionBody(Today, [Squat(100), new { name = "Plank", sets = new[] { new { durationSec = 60 } } }], title: "Legs"));

        var session = (await Member.GetFromJsonAsync<Session>($"/api/v1/training/sessions/{saved.Id}", Json, Ct))!;
        Assert.Equal("Legs", session.Title);
        Assert.Null(session.ProgramId);
        Assert.Equal(["back-squat", "plank"], session.Exercises.Select(e => e.ExerciseKey));
        Assert.Equal(116.7m, session.Exercises[0].BestE1RmKg);
        Assert.Null(session.Exercises[1].TopWeightKg);

        var list = (await Member.GetFromJsonAsync<PagedResult<Summary>>("/api/v1/training/sessions", Json, Ct))!;
        var summary = Assert.Single(list.Data);
        Assert.Equal((2, 3, 950m), (summary.ExerciseCount, summary.SetCount, summary.VolumeKg));
        Assert.Equal(["back squat", "Plank"], summary.ExerciseNames);

        var stranger = factory.ClientFor($"member-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/training/sessions/{saved.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/training/sessions/{saved.Id}", Ct)).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<PagedResult<Summary>>("/api/v1/training/sessions", Json, Ct))!.Data);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientFor(null).GetAsync("/api/v1/training/sessions", Ct)).StatusCode);
    }

    [Fact]
    public async Task Invalid_sessions_are_rejected()
    {
        var emptySet = SessionBody(Today, [new { name = "Squat", sets = new[] { new { } } }]);
        var future = SessionBody(Today.AddDays(5), [Squat(100)]);
        var noExercises = SessionBody(Today, []);

        Assert.Equal(HttpStatusCode.BadRequest, (await Member.PostAsJsonAsync("/api/v1/training/sessions", emptySet, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Member.PostAsJsonAsync("/api/v1/training/sessions", future, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Member.PostAsJsonAsync("/api/v1/training/sessions", noExercises, Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Program_sessions_need_access_and_a_released_workout_and_snapshot_the_program()
    {
        var program = await ProgramAsync(enrollMember: false);
        var body = SessionBody(Today, [Squat(100, name: "my squat", programExerciseId: program.SquatId)], program.ProgramId, program.WorkoutId);

        Assert.Equal(HttpStatusCode.Forbidden, (await Member.PostAsJsonAsync("/api/v1/training/sessions", body, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Member.PutAsync($"/api/v1/subscriptions/programs/{program.ProgramId}", null, Ct)).StatusCode);

        // Next week's workout isn't released yet.
        var early = SessionBody(Today, [Squat(100)], program.ProgramId, program.FutureWorkoutId);
        Assert.Equal(HttpStatusCode.NotFound, (await Member.PostAsJsonAsync("/api/v1/training/sessions", early, Json, Ct)).StatusCode);

        // An exercise id from another workout.
        var foreign = SessionBody(Today, [Squat(100, programExerciseId: Guid.NewGuid())], program.ProgramId, program.WorkoutId);
        Assert.Equal(HttpStatusCode.BadRequest, (await Member.PostAsJsonAsync("/api/v1/training/sessions", foreign, Json, Ct)).StatusCode);

        var saved = await LogAsync(Member, body);
        var session = (await Member.GetFromJsonAsync<Session>($"/api/v1/training/sessions/{saved.Id}", Json, Ct))!;
        Assert.Equal("Squat day", session.Title);
        Assert.StartsWith("Squat Lab", session.ProgramTitle);
        Assert.Equal((1, program.WorkoutId), (session.WeekNumber, session.ProgramWorkoutId));
        var squat = Assert.Single(session.Exercises);
        Assert.Equal(("Back Squat", program.SquatId), (squat.Name, squat.ProgramExerciseId));

        // Editing keeps the program exercise's name and can't add another program's exercise.
        var edit = SessionBody(Today, [Squat(105, name: "renamed", programExerciseId: program.SquatId), Squat(50, name: "Lunge")]);
        Assert.Equal(HttpStatusCode.OK, (await Member.PutAsJsonAsync($"/api/v1/training/sessions/{saved.Id}", edit, Json, Ct)).StatusCode);
        session = (await Member.GetFromJsonAsync<Session>($"/api/v1/training/sessions/{saved.Id}", Json, Ct))!;
        Assert.Equal(["Back Squat", "Lunge"], session.Exercises.Select(e => e.Name));
        Assert.Equal(program.WorkoutId, session.ProgramWorkoutId);

        var foreignEdit = SessionBody(Today, [Squat(100, programExerciseId: Guid.NewGuid())]);
        Assert.Equal(HttpStatusCode.BadRequest, (await Member.PutAsJsonAsync($"/api/v1/training/sessions/{saved.Id}", foreignEdit, Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Personal_records_are_flagged_on_log_and_follow_edits_and_deletes()
    {
        var first = await LogAsync(Member, SessionBody(Today.AddDays(-14), [Squat(100)]));
        Assert.Empty(first.Records);

        var second = await LogAsync(Member, SessionBody(Today.AddDays(-7), [Squat(110)]));
        Assert.Equal(
            [("Weight", 110m, 100m), ("E1Rm", 128.3m, 116.7m)],
            second.Records.Select(r => (r.Kind, r.Value, r.Previous)));

        // A lighter session today sets nothing.
        var third = await LogAsync(Member, SessionBody(Today, [Squat(105, name: "Back  Squat")]));
        Assert.Empty(third.Records);

        // Editing today's session past the best sets both records again.
        var edited = await Member.PutAsJsonAsync($"/api/v1/training/sessions/{third.Id}", SessionBody(Today, [Squat(120)]), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(
            [("Weight", 120m, 110m), ("E1Rm", 140m, 128.3m)],
            (await edited.Content.ReadFromJsonAsync<Saved>(Json, Ct))!.Records.Select(r => (r.Kind, r.Value, r.Previous)));

        // The key is normalized, so any spelling of the name finds the same progress line.
        var progress = (await Member.GetFromJsonAsync<Progress>("/api/v1/training/exercises/Back%20Squat/progress", Json, Ct))!;
        Assert.Equal("back-squat", progress.Key);
        Assert.Equal(3, progress.Points.Count);
        Assert.Equal(120m, progress.BestWeightKg);
        Assert.Equal([110m, 120m], progress.Records.Where(r => r.Kind == "Weight").Select(r => r.Value));

        // Deleting the middle session makes today's session the only record breaker over the first.
        Assert.Equal(HttpStatusCode.NoContent, (await Member.DeleteAsync($"/api/v1/training/sessions/{second.Id}", Ct)).StatusCode);
        progress = (await Member.GetFromJsonAsync<Progress>("/api/v1/training/exercises/back-squat/progress", Json, Ct))!;
        Assert.Equal(2, progress.Points.Count);
        var weight = Assert.Single(progress.Records, r => r.Kind == "Weight");
        Assert.Equal((120m, 100m), (weight.Value, weight.Previous));

        var exercises = (await Member.GetFromJsonAsync<List<ExerciseSummary>>("/api/v1/training/exercises", Json, Ct))!;
        var squat = Assert.Single(exercises);
        Assert.Equal(("back squat", 2, 120m), (squat.Name, squat.SessionCount, squat.BestWeightKg));

        Assert.Equal(HttpStatusCode.NotFound, (await Member.GetAsync("/api/v1/training/exercises/deadlift/progress", Ct)).StatusCode);
    }

    [Fact]
    public async Task Adherence_marks_done_workouts_and_lists_sessions_for_removed_ones()
    {
        var program = await ProgramAsync();
        var saved = await LogAsync(Member, SessionBody(Today, [Squat(100, programExerciseId: program.SquatId)], program.ProgramId, program.WorkoutId));

        var adherence = (await Member.GetFromJsonAsync<Adherence>($"/api/v1/training/programs/{program.ProgramId}/adherence", Json, Ct))!;
        var week = Assert.Single(adherence.Weeks);
        Assert.Equal((1, 1, 1), (week.WeekNumber, week.Planned, week.Completed));
        Assert.Equal(saved.Id, Assert.Single(week.Workouts).LastSessionId);
        Assert.Empty(adherence.NoLongerInProgram);

        // The trainer removes the workout; the session stays in the log and is listed apart.
        var removed = await Trainer.DeleteAsync($"/api/v1/programs/{program.ProgramId}/weeks/{program.WeekId}/days/1/workouts/{program.WorkoutId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        adherence = (await Member.GetFromJsonAsync<Adherence>($"/api/v1/training/programs/{program.ProgramId}/adherence", Json, Ct))!;
        Assert.Equal((0, 0), (adherence.Planned, adherence.Completed));
        Assert.Equal(saved.Id, Assert.Single(adherence.NoLongerInProgram).SessionId);
        Assert.Equal(HttpStatusCode.OK, (await Member.GetAsync($"/api/v1/training/sessions/{saved.Id}", Ct)).StatusCode);

        var outsider = factory.ClientFor($"member-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/training/programs/{program.ProgramId}/adherence", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/training/programs/{Guid.NewGuid()}/adherence", Ct)).StatusCode);
    }
}
