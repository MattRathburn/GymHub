using GymHub.BuildingBlocks.Results;

namespace GymHub.Modules.Discussions.Domain;

public enum ReportReason
{
    Spam,
    Abuse,
    OffTopic,
    Other
}

public enum ReportStatus
{
    Open,
    Dismissed,
    Actioned
}

/// <summary>A user flags a post for admins. A reporter has at most one open report per post.</summary>
public sealed class PostReport
{
    public const int MaxNoteLength = 500;

    private PostReport() { }

    public Guid Id { get; private set; }
    public Guid PostId { get; private set; }
    public Guid ThreadId { get; private set; }
    public Guid ProgramId { get; private set; }
    public string ReporterId { get; private set; } = "";
    public ReportReason Reason { get; private set; }
    public string? Note { get; private set; }
    public ReportStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }

    public static Result<PostReport> File(Post post, Guid programId, string reporterId, ReportReason reason, string? note, DateTime now)
    {
        if (post.AuthorId == reporterId)
        {
            return Error.Invalid("You can't report your own post.");
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (trimmed?.Length > MaxNoteLength)
        {
            return Error.Invalid($"Notes are at most {MaxNoteLength} characters.");
        }

        return new PostReport
        {
            Id = Guid.CreateVersion7(),
            PostId = post.Id,
            ThreadId = post.ThreadId,
            ProgramId = programId,
            ReporterId = reporterId,
            Reason = reason,
            Note = trimmed,
            Status = ReportStatus.Open,
            CreatedAt = now
        };
    }

    public Result Resolve(ReportStatus outcome, string adminId, DateTime now)
    {
        if (outcome == ReportStatus.Open)
        {
            return Error.Invalid("A report can only be dismissed or actioned.");
        }

        if (Status != ReportStatus.Open)
        {
            return Error.Conflict($"This report was already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = outcome;
        ResolvedBy = adminId;
        ResolvedAt = now;
        return Result.Success();
    }
}
