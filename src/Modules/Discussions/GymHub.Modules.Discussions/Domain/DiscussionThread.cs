using EventBus.Events;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Discussions.Contracts;

namespace GymHub.Modules.Discussions.Domain;

public enum ThreadKind
{
    Question,
    General
}

/// <summary>
/// A conversation on a program's board. Its opening post and replies are <see cref="Post"/>s; replies form a tree
/// (a reply may answer another reply) up to <see cref="MaxDepth"/> levels below the opening post.
/// Who may moderate is decided by the caller (the program's trainer or an admin) and passed in.
/// </summary>
public sealed class DiscussionThread
{
    public const int MaxTitleLength = 150;
    public const int MaxDepth = 8;

    private readonly List<IntegrationEvent> _events = [];

    private DiscussionThread() { }

    public Guid Id { get; private set; }
    public Guid ProgramId { get; private set; }
    public string AuthorId { get; private set; } = "";
    public ThreadKind Kind { get; private set; }
    public string Title { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public DateTime? PinnedAt { get; private set; }
    public DateTime? LockedAt { get; private set; }
    public DateTime? HiddenAt { get; private set; }
    public Guid? AnswerPostId { get; private set; }

    public bool IsPinned => PinnedAt is not null;
    public bool IsLocked => LockedAt is not null;
    public bool IsHidden => HiddenAt is not null;

    internal IReadOnlyList<IntegrationEvent> Events => _events;

    internal void ClearEvents() => _events.Clear();

    /// <summary>Starts a thread together with its opening post.</summary>
    public static Result<(DiscussionThread Thread, Post OpeningPost)> Start(
        Guid programId, ThreadKind kind, string title, string body, string authorId, string authorName, DateTime now)
    {
        var trimmedTitle = title.Trim();

        if (trimmedTitle.Length == 0 || trimmedTitle.Length > MaxTitleLength)
        {
            return Error.Invalid($"Titles are 1–{MaxTitleLength} characters.");
        }

        var thread = new DiscussionThread
        {
            Id = Guid.CreateVersion7(),
            ProgramId = programId,
            AuthorId = authorId,
            Kind = kind,
            Title = trimmedTitle,
            CreatedAt = now,
            LastActivityAt = now
        };

        var post = Post.Create(thread.Id, parent: null, authorId, authorName, body, now);

        if (!post.IsSuccess)
        {
            return post.Error!;
        }

        thread._events.Add(new DiscussionThreadStarted(thread.Id, programId, authorId, trimmedTitle));
        return (thread, post.Value!);
    }

    /// <summary>Replies to the opening post or to another reply. Moderators may still reply in a locked thread.</summary>
    public Result<Post> Reply(Post parent, string authorId, string authorName, string body, bool canModerate, DateTime now)
    {
        if (parent.ThreadId != Id)
        {
            return Error.NotFound("That post isn't in this thread.");
        }

        if (IsHidden)
        {
            return Error.Conflict("This thread was removed by a moderator.");
        }

        if (IsLocked && !canModerate)
        {
            return Error.Conflict("This thread is locked.");
        }

        if (parent.IsDeleted || parent.IsHidden)
        {
            return Error.Conflict("You can't reply to a removed post.");
        }

        if (parent.Depth >= MaxDepth)
        {
            return Error.Invalid($"Replies can nest at most {MaxDepth} levels deep.");
        }

        var reply = Post.Create(Id, parent, authorId, authorName, body, now);

        if (!reply.IsSuccess)
        {
            return reply.Error!;
        }

        LastActivityAt = now;
        _events.Add(new DiscussionReplyPosted(reply.Value!.Id, Id, ProgramId, authorId, parent.AuthorId));
        return reply;
    }

    /// <summary>Marks a reply as the answer, or clears the answer when <paramref name="post"/> is null.</summary>
    public Result MarkAnswer(Post? post, string actorId, bool canModerate)
    {
        if (Kind != ThreadKind.Question)
        {
            return Error.Invalid("Only question threads have answers.");
        }

        if (actorId != AuthorId && !canModerate)
        {
            return Error.Forbidden("Only the person who asked or a moderator can choose the answer.");
        }

        if (post is null)
        {
            AnswerPostId = null;
            return Result.Success();
        }

        if (post.ThreadId != Id || post.IsOpeningPost)
        {
            return Error.Invalid("The answer must be a reply in this thread.");
        }

        if (post.IsDeleted || post.IsHidden)
        {
            return Error.Conflict("A removed post can't be the answer.");
        }

        if (AnswerPostId == post.Id)
        {
            return Result.Success();
        }

        AnswerPostId = post.Id;
        _events.Add(new DiscussionAnswerMarked(Id, post.Id, ProgramId, post.AuthorId));
        return Result.Success();
    }

    /// <summary>Called when a post is deleted outright, so the thread doesn't point at it.</summary>
    public void PostRemoved(Guid postId)
    {
        if (AnswerPostId == postId)
        {
            AnswerPostId = null;
        }
    }

    public void SetPinned(bool pinned, DateTime now) => PinnedAt = pinned ? PinnedAt ?? now : null;

    public void SetLocked(bool locked, DateTime now) => LockedAt = locked ? LockedAt ?? now : null;

    public void SetHidden(bool hidden, DateTime now) => HiddenAt = hidden ? HiddenAt ?? now : null;
}
