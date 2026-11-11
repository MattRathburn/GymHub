using EventBus.Events;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;

namespace GymHub.Modules.Programs.Domain;

public enum ProgramKind
{
    /// <summary>Runs from StartsOn to EndsOn.</summary>
    Finite,

    /// <summary>No end; the trainer keeps scheduling weeks.</summary>
    Ongoing
}

/// <summary>
/// Aggregate root of the Programs module. Content is released by calendar date: each week has a ReleasesOn date
/// that applies to every subscriber.
/// </summary>
public sealed class TrainingProgram
{
    private readonly List<ProgramWeek> _weeks = [];
    private readonly List<PriceVersion> _prices = [];
    private readonly List<MediaAttachment> _media = [];
    private readonly List<IntegrationEvent> _events = [];

    private TrainingProgram() { }

    public Guid Id { get; private set; }
    public string TrainerId { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string? Summary { get; private set; }
    public string? Description { get; private set; }
    public string? CoverObjectKey { get; private set; }
    public ProgramKind Kind { get; private set; }
    public ProgramStatus Status { get; private set; }
    public DateOnly? StartsOn { get; private set; }
    public DateOnly? EndsOn { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    /// <summary>Mirrors CurrentPrice.Price.IsFree so the catalog can filter on it in SQL.</summary>
    public bool IsFree { get; private set; } = true;

    public IReadOnlyList<ProgramWeek> Weeks => _weeks;
    public IReadOnlyList<PriceVersion> Prices => _prices;
    public IReadOnlyList<MediaAttachment> Media => _media;

    public PriceVersion CurrentPrice => _prices.MaxBy(p => p.Version)!;

    internal IReadOnlyList<IntegrationEvent> Events => _events;

    internal void ClearEvents() => _events.Clear();

    public static Result<TrainingProgram> Create(
        string trainerId, string title, string? summary, string? description,
        ProgramKind kind, DateOnly? startsOn, DateOnly? endsOn, DateTime now)
    {
        if (ValidateSchedule(kind, startsOn, endsOn) is { } error)
        {
            return error;
        }

        var program = new TrainingProgram
        {
            Id = Guid.CreateVersion7(),
            TrainerId = trainerId,
            Title = title,
            Summary = summary,
            Description = description,
            Kind = kind,
            StartsOn = startsOn,
            EndsOn = kind == ProgramKind.Finite ? endsOn : null,
            Status = ProgramStatus.Draft,
            CreatedAt = now
        };

        program._prices.Add(new PriceVersion(1, ProgramPrice.Free, now));
        return program;
    }

    public bool IsOwnedBy(string userId) => TrainerId == userId;

    public Result UpdateDetails(string title, string? summary, string? description, ProgramKind kind, DateOnly? startsOn, DateOnly? endsOn)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        var scheduleChanged = kind != Kind || startsOn != StartsOn || (kind == ProgramKind.Finite && endsOn != EndsOn);

        if (scheduleChanged && Status != ProgramStatus.Draft)
        {
            return Error.Conflict("The kind and dates of a published program can't be changed.");
        }

        if (ValidateSchedule(kind, startsOn, endsOn) is { } invalid)
        {
            return invalid;
        }

        Title = title;
        Summary = summary;
        Description = description;
        Kind = kind;
        StartsOn = startsOn;
        EndsOn = kind == ProgramKind.Finite ? endsOn : null;
        return Result.Success();
    }

    /// <summary>
    /// While drafting, the single price is replaced. Once published, a new price version is added so that existing
    /// purchases keep the price they were sold at.
    /// </summary>
    public Result SetPricing(ProgramPrice price, DateTime now)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        if (ValidatePrice(price) is { } invalid)
        {
            return invalid;
        }

        var current = CurrentPrice;

        if (current.Price == price)
        {
            return Result.Success();
        }

        // Stripe subscriptions can't switch currency, so existing subscribers couldn't move to the new price.
        if (Status == ProgramStatus.Published
            && current.Price.Model == PricingModel.Recurring
            && price.Model == PricingModel.Recurring
            && price.Currency != current.Price.Currency)
        {
            return Error.Conflict("The currency of a published subscription program can't be changed.");
        }

        IsFree = price.IsFree;

        if (Status == ProgramStatus.Draft)
        {
            current.Replace(price, now);
            return Result.Success();
        }

        var next = new PriceVersion(current.Version + 1, price, now);
        _prices.Add(next);
        _events.Add(new ProgramPricingChanged(Id, price, next.Version));
        return Result.Success();
    }

    public Result<ProgramWeek> AddWeek(string title, DateOnly releasesOn)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        if (ValidateReleaseDate(releasesOn) is { } invalid)
        {
            return invalid;
        }

        var week = new ProgramWeek(_weeks.Count + 1, title, releasesOn);
        _weeks.Add(week);
        return week;
    }

    public Result UpdateWeek(Guid weekId, string title, DateOnly releasesOn)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        if (FindWeek(weekId) is not { } week)
        {
            return Error.NotFound("Week not found.");
        }

        if (ValidateReleaseDate(releasesOn) is { } invalid)
        {
            return invalid;
        }

        week.Update(title, releasesOn);
        return Result.Success();
    }

    public Result RemoveWeek(Guid weekId, DateOnly today)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        if (FindWeek(weekId) is not { } week)
        {
            return Error.NotFound("Week not found.");
        }

        if (Status == ProgramStatus.Published && week.IsReleased(today))
        {
            return Error.Conflict("A week subscribers can already see can't be removed.");
        }

        _weeks.Remove(week);
        _media.RemoveAll(m => m.TargetKind == MediaTarget.Week && m.TargetId == weekId);

        var number = 1;
        foreach (var remaining in _weeks.OrderBy(w => w.Number))
        {
            remaining.Renumber(number++);
        }

        return Result.Success();
    }

    public Result<ProgramDay> UpsertDay(Guid weekId, int dayNumber, string? title, bool isRestDay, string? notes)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        if (FindWeek(weekId) is not { } week)
        {
            return Error.NotFound("Week not found.");
        }

        return week.UpsertDay(dayNumber, title, isRestDay, notes);
    }

    public Result<Workout> SaveWorkout(Guid weekId, int dayNumber, Guid? workoutId, string title, string? notes, IReadOnlyList<ExerciseDraft> exercises)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        if (FindWeek(weekId)?.FindDay(dayNumber) is not { } day)
        {
            return Error.NotFound("Day not found. Create the day first.");
        }

        if (day.IsRestDay)
        {
            return Error.Conflict("Rest days can't have workouts.");
        }

        return day.SaveWorkout(workoutId, title, notes, exercises);
    }

    public Result RemoveWorkout(Guid weekId, int dayNumber, Guid workoutId)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        if (FindWeek(weekId)?.FindDay(dayNumber) is not { } day || !day.RemoveWorkout(workoutId))
        {
            return Error.NotFound("Workout not found.");
        }

        _media.RemoveAll(m => m.TargetKind == MediaTarget.Workout && m.TargetId == workoutId);
        return Result.Success();
    }

    public Result<MediaAttachment> AttachMedia(MediaTarget target, Guid? targetId, string objectKey, string contentType, string? caption)
    {
        if (EnsureEditable() is { } notEditable)
        {
            return notEditable;
        }

        if (!objectKey.StartsWith(MediaKeyPrefix, StringComparison.Ordinal))
        {
            return Error.Invalid("The media object doesn't belong to this program.");
        }

        var kind = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? MediaKind.Image
            : contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? MediaKind.Video
            : (MediaKind?)null;

        if (kind is null)
        {
            return Error.Invalid("Only image and video media are supported.");
        }

        if (target == MediaTarget.Cover)
        {
            if (kind != MediaKind.Image)
            {
                return Error.Invalid("The cover must be an image.");
            }

            CoverObjectKey = objectKey;
            return new MediaAttachment(target, Id, objectKey, contentType, kind.Value, caption);
        }

        var resolvedTargetId = target == MediaTarget.Program ? Id : targetId;

        if (resolvedTargetId is null || !TargetExists(target, resolvedTargetId.Value))
        {
            return Error.NotFound("The item to attach media to was not found.");
        }

        var attachment = new MediaAttachment(target, resolvedTargetId.Value, objectKey, contentType, kind.Value, caption);
        _media.Add(attachment);
        return attachment;
    }

    /// <summary>Object keys for this program's media all start with this prefix.</summary>
    public string MediaKeyPrefix => $"programs/{Id:N}/";

    public Result Publish(DateTime now)
    {
        if (Status != ProgramStatus.Draft)
        {
            return Error.Conflict("Only draft programs can be published.");
        }

        if (_weeks.Count == 0)
        {
            return Error.Invalid("Add at least one week before publishing.");
        }

        if (Kind == ProgramKind.Finite && _weeks.Any(w => w.ReleasesOn < StartsOn || w.ReleasesOn > EndsOn))
        {
            return Error.Invalid("Every week must release between the program's start and end dates.");
        }

        Status = ProgramStatus.Published;
        PublishedAt = now;

        // Weeks already out at publish time have nobody to notify yet.
        var today = DateOnly.FromDateTime(now);
        foreach (var week in _weeks.Where(w => w.IsReleased(today)))
        {
            week.MarkReleaseAnnounced(now);
        }

        var price = CurrentPrice;
        _events.Add(new ProgramPublished(Id, TrainerId, Title, price.Price, price.Version));
        return Result.Success();
    }

    public Result Archive()
    {
        if (Status == ProgramStatus.Archived)
        {
            return Error.Conflict("The program is already archived.");
        }

        var wasPublished = Status == ProgramStatus.Published;
        Status = ProgramStatus.Archived;

        if (wasPublished)
        {
            _events.Add(new ProgramArchived(Id));
        }

        return Result.Success();
    }

    /// <summary>Announces weeks whose release date has arrived. Run daily by the release job.</summary>
    public int AnnounceReleasedWeeks(DateTime now)
    {
        if (Status != ProgramStatus.Published)
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(now);
        var count = 0;

        foreach (var week in _weeks.Where(w => w.IsReleased(today) && w.ReleaseAnnouncedAt is null))
        {
            week.MarkReleaseAnnounced(now);
            _events.Add(new ProgramWeekReleased(Id, week.Id, week.Number, week.Title, week.ReleasesOn));
            count++;
        }

        return count;
    }

    public IEnumerable<ProgramWeek> ReleasedWeeks(DateOnly today) =>
        _weeks.Where(w => w.IsReleased(today)).OrderBy(w => w.Number);

    private ProgramWeek? FindWeek(Guid weekId) => _weeks.FirstOrDefault(w => w.Id == weekId);

    private bool TargetExists(MediaTarget target, Guid targetId) => target switch
    {
        MediaTarget.Program => targetId == Id,
        MediaTarget.Week => _weeks.Any(w => w.Id == targetId),
        MediaTarget.Day => _weeks.SelectMany(w => w.Days).Any(d => d.Id == targetId),
        MediaTarget.Workout => _weeks.SelectMany(w => w.Days).SelectMany(d => d.Workouts).Any(w => w.Id == targetId),
        MediaTarget.Exercise => _weeks.SelectMany(w => w.Days).SelectMany(d => d.Workouts).SelectMany(w => w.Exercises).Any(e => e.Id == targetId),
        _ => false
    };

    private Error? EnsureEditable() =>
        Status == ProgramStatus.Archived ? Error.Conflict("Archived programs can't be changed.") : null;

    private Error? ValidateReleaseDate(DateOnly releasesOn)
    {
        if (Kind == ProgramKind.Finite && (releasesOn < StartsOn || releasesOn > EndsOn))
        {
            return Error.Invalid("The week must release between the program's start and end dates.");
        }

        return null;
    }

    private static Error? ValidateSchedule(ProgramKind kind, DateOnly? startsOn, DateOnly? endsOn)
    {
        if (kind == ProgramKind.Finite)
        {
            if (startsOn is null || endsOn is null)
            {
                return Error.Invalid("Finite programs need a start date and an end date.");
            }

            if (endsOn < startsOn)
            {
                return Error.Invalid("The end date must be on or after the start date.");
            }
        }

        return null;
    }

    private static Error? ValidatePrice(ProgramPrice price) => price.Model switch
    {
        PricingModel.Free when price.AmountMinor is not null || price.Interval is not null
            => Error.Invalid("Free programs have no amount or billing interval."),
        PricingModel.OneTime when price.AmountMinor is not > 0 || string.IsNullOrEmpty(price.Currency) || price.Interval is not null
            => Error.Invalid("One-time pricing needs a positive amount and a currency, and no billing interval."),
        PricingModel.Recurring when price.AmountMinor is not > 0 || string.IsNullOrEmpty(price.Currency) || price.Interval is null
            => Error.Invalid("Recurring pricing needs a positive amount, a currency and a billing interval."),
        PricingModel.Recurring when price.TrialDays is < 0 or > ProgramPrice.MaxTrialDays
            => Error.Invalid($"A free trial can be up to {ProgramPrice.MaxTrialDays} days."),
        not PricingModel.Recurring when price.TrialDays is not null
            => Error.Invalid("Only subscription programs can have a free trial."),
        _ => null
    };
}
