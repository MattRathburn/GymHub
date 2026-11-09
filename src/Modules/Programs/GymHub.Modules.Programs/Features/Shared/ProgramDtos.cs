using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Domain;

namespace GymHub.Modules.Programs.Features.Shared;

public sealed record MediaDto(Guid Id, MediaKind Kind, string ContentType, string? Caption, Uri Url);

public sealed record ExerciseDto(Guid Id, int Order, string Name, string? Notes, IReadOnlyList<ExerciseSet> Sets, IReadOnlyList<MediaDto> Media);

public sealed record WorkoutDto(Guid Id, int Order, string Title, string? Notes, IReadOnlyList<ExerciseDto> Exercises, IReadOnlyList<MediaDto> Media);

public sealed record DayDto(Guid Id, int Number, string? Title, bool IsRestDay, string? Notes, IReadOnlyList<WorkoutDto> Workouts, IReadOnlyList<MediaDto> Media);

public sealed record WeekDto(Guid Id, int Number, string Title, DateOnly ReleasesOn, bool IsReleased, IReadOnlyList<DayDto> Days, IReadOnlyList<MediaDto> Media);

public sealed record ProgramContentDto(
    Guid Id, string TrainerId, string Title, string? Summary, string? Description,
    ProgramKind Kind, ProgramStatus Status, DateOnly? StartsOn, DateOnly? EndsOn,
    ProgramPrice Price, Uri? CoverUrl, IReadOnlyList<MediaDto> Media, IReadOnlyList<WeekDto> Weeks);

/// <summary>Maps a loaded program into DTOs, signing media URLs. Shared by the content and editing slices.</summary>
internal sealed class ProgramContentMapper(IObjectStorage storage, TimeProvider timeProvider)
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(1);

    public DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public Uri? CoverUrl(TrainingProgram program) =>
        program.CoverObjectKey is null ? null : storage.CreateDownloadUrl(program.CoverObjectKey, UrlLifetime);

    public IReadOnlyList<MediaDto> MediaFor(TrainingProgram program, MediaTarget target, Guid targetId) =>
        [.. program.Media
            .Where(m => m.TargetKind == target && m.TargetId == targetId)
            .Select(m => new MediaDto(m.Id, m.Kind, m.ContentType, m.Caption, storage.CreateDownloadUrl(m.ObjectKey, UrlLifetime)))];

    public ProgramContentDto ToContent(TrainingProgram program, IEnumerable<ProgramWeek> weeks)
    {
        var today = Today;

        return new ProgramContentDto(
            program.Id, program.TrainerId, program.Title, program.Summary, program.Description,
            program.Kind, program.Status, program.StartsOn, program.EndsOn,
            program.CurrentPrice.Price, CoverUrl(program),
            MediaFor(program, MediaTarget.Program, program.Id),
            [.. weeks.OrderBy(w => w.Number).Select(w => ToWeek(program, w, today))]);
    }

    private WeekDto ToWeek(TrainingProgram program, ProgramWeek week, DateOnly today) => new(
        week.Id, week.Number, week.Title, week.ReleasesOn, week.IsReleased(today),
        [.. week.Days.OrderBy(d => d.Number).Select(d => new DayDto(
            d.Id, d.Number, d.Title, d.IsRestDay, d.Notes,
            [.. d.Workouts.OrderBy(w => w.Order).Select(w => new WorkoutDto(
                w.Id, w.Order, w.Title, w.Notes,
                [.. w.Exercises.OrderBy(e => e.Order).Select(e => new ExerciseDto(
                    e.Id, e.Order, e.Name, e.Notes, e.Sets, MediaFor(program, MediaTarget.Exercise, e.Id)))],
                MediaFor(program, MediaTarget.Workout, w.Id)))],
            MediaFor(program, MediaTarget.Day, d.Id)))],
        MediaFor(program, MediaTarget.Week, week.Id));
}
