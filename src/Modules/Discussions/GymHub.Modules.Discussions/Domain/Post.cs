using GymHub.BuildingBlocks.Results;

namespace GymHub.Modules.Discussions.Domain;

/// <summary>
/// A thread's opening post (no parent, depth 0) or a reply. Plain text. Deleting a post that has replies keeps it
/// as an empty placeholder so the reply tree stays intact.
/// </summary>
public sealed class Post
{
    public const int MaxBodyLength = 5000;
    public const int MaxAuthorNameLength = 100;

    private Post() { }

    public Guid Id { get; private set; }
    public Guid ThreadId { get; private set; }
    public Guid? ParentPostId { get; private set; }
    public int Depth { get; private set; }
    public string AuthorId { get; private set; } = "";

    /// <summary>The author's username when they posted; shown when they have no profile.</summary>
    public string AuthorName { get; private set; } = "";

    public string Body { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public DateTime? EditedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public DateTime? HiddenAt { get; private set; }
    public string? HiddenBy { get; private set; }

    public bool IsOpeningPost => ParentPostId is null;
    public bool IsDeleted => DeletedAt is not null;
    public bool IsHidden => HiddenAt is not null;

    internal static Result<Post> Create(Guid threadId, Post? parent, string authorId, string authorName, string body, DateTime now)
    {
        var text = NormalizeBody(body);

        if (!text.IsSuccess)
        {
            return text.Error!;
        }

        return new Post
        {
            Id = Guid.CreateVersion7(),
            ThreadId = threadId,
            ParentPostId = parent?.Id,
            Depth = parent is null ? 0 : parent.Depth + 1,
            AuthorId = authorId,
            AuthorName = Truncate(authorName.Trim(), MaxAuthorNameLength),
            Body = text.Value!,
            CreatedAt = now
        };
    }

    /// <summary>The author edits their post. Nobody but a moderator may post in a locked thread, so edits stop too.</summary>
    public Result Edit(string userId, string body, bool threadLocked, DateTime now)
    {
        if (userId != AuthorId)
        {
            return Error.Forbidden("You can only edit your own posts.");
        }

        if (IsDeleted || IsHidden)
        {
            return Error.Conflict("This post was removed.");
        }

        if (threadLocked)
        {
            return Error.Conflict("This thread is locked.");
        }

        var text = NormalizeBody(body);

        if (!text.IsSuccess)
        {
            return text.Error!;
        }

        Body = text.Value!;
        EditedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// The author deletes their post. Returns true when the post should be removed outright (no replies),
    /// false when it stays as a placeholder with its text wiped.
    /// </summary>
    public Result<bool> Delete(string userId, bool hasReplies, DateTime now)
    {
        if (userId != AuthorId)
        {
            return Error.Forbidden("You can only delete your own posts.");
        }

        if (IsDeleted)
        {
            return false;
        }

        // A hidden post stays for moderators to review; letting its author delete it would also drop its reports.
        if (IsHidden)
        {
            return Error.Conflict("This post was removed by a moderator.");
        }

        if (!hasReplies)
        {
            return true;
        }

        Body = "";
        DeletedAt = now;
        return false;
    }

    public void SetHidden(bool hidden, string moderatorId, DateTime now)
    {
        if (hidden)
        {
            HiddenAt ??= now;
            HiddenBy = moderatorId;
        }
        else
        {
            HiddenAt = null;
            HiddenBy = null;
        }
    }

    private static Result<string> NormalizeBody(string body)
    {
        var text = body.Trim();

        if (text.Length == 0 || text.Length > MaxBodyLength)
        {
            return Error.Invalid($"Posts are 1–{MaxBodyLength} characters.");
        }

        return text;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
