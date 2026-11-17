using GymHub.BuildingBlocks.Results;

namespace GymHub.Modules.Reviews.Domain;

/// <summary>
/// A member's rating of a program: 1 to 5 stars and optional text, one per member per program. The program's trainer
/// can add one reply; admins hide reviews that break the rules (hidden reviews don't count toward the rating).
/// </summary>
public sealed class Review
{
    public const int MinStars = 1;
    public const int MaxStars = 5;
    public const int MaxBodyLength = 2_000;
    public const int MaxReplyLength = 2_000;
    public const int MaxNameLength = 100;

    private Review() { }

    public Guid Id { get; private set; }
    public Guid ProgramId { get; private set; }
    public string TrainerId { get; private set; } = "";
    public string AuthorId { get; private set; } = "";

    /// <summary>The author's username when they wrote it, shown if they have no profile.</summary>
    public string AuthorName { get; private set; } = "";

    public int Stars { get; private set; }
    public string? Body { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsHidden { get; private set; }
    public string? HiddenBy { get; private set; }
    public DateTime? HiddenAt { get; private set; }

    public string? Reply { get; private set; }
    public DateTime? RepliedAt { get; private set; }
    public DateTime? ReplyUpdatedAt { get; private set; }

    public static Result<Review> Write(
        Guid programId, string trainerId, string authorId, string authorName, int stars, string? body, DateTime now)
    {
        if (authorId == trainerId)
        {
            return Error.Forbidden("You can't review your own program.");
        }

        var checkedBody = Check(stars, body);

        if (!checkedBody.IsSuccess)
        {
            return checkedBody.Error!;
        }

        var name = authorName.Trim();

        return new Review
        {
            Id = Guid.CreateVersion7(),
            ProgramId = programId,
            TrainerId = trainerId,
            AuthorId = authorId,
            AuthorName = name.Length > MaxNameLength ? name[..MaxNameLength] : name,
            Stars = stars,
            Body = checkedBody.Value,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public Result Edit(int stars, string? body, DateTime now)
    {
        var checkedBody = Check(stars, body);

        if (!checkedBody.IsSuccess)
        {
            return checkedBody.Error!;
        }

        Stars = stars;
        Body = checkedBody.Value;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result SetReply(string body, DateTime now)
    {
        var trimmed = body.Trim();

        if (trimmed.Length == 0)
        {
            return Error.Invalid("A reply needs some text.");
        }

        if (trimmed.Length > MaxReplyLength)
        {
            return Error.Invalid($"Replies are at most {MaxReplyLength} characters.");
        }

        if (Reply is null)
        {
            RepliedAt = now;
        }

        Reply = trimmed;
        ReplyUpdatedAt = now;
        return Result.Success();
    }

    public void RemoveReply()
    {
        Reply = null;
        RepliedAt = null;
        ReplyUpdatedAt = null;
    }

    public void SetHidden(bool hidden, string adminId, DateTime now)
    {
        IsHidden = hidden;
        HiddenBy = hidden ? adminId : null;
        HiddenAt = hidden ? now : null;
    }

    private static Result<string?> Check(int stars, string? body)
    {
        if (stars is < MinStars or > MaxStars)
        {
            return Error.Invalid($"Choose {MinStars} to {MaxStars} stars.");
        }

        var trimmed = string.IsNullOrWhiteSpace(body) ? null : body.Trim();

        if (trimmed?.Length > MaxBodyLength)
        {
            return Error.Invalid($"Reviews are at most {MaxBodyLength} characters.");
        }

        return trimmed;
    }
}
