using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Discussions.Contracts;
using GymHub.Modules.Discussions.Domain;

namespace GH.UnitTests.Discussions;

public class DiscussionThreadTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ProgramId = Guid.CreateVersion7();

    private static (DiscussionThread Thread, Post Opening) Question()
    {
        var started = DiscussionThread.Start(ProgramId, ThreadKind.Question, "  How deep should I squat?  ", "Body", "asker", "Asker", Now);
        Assert.True(started.IsSuccess);
        return started.Value;
    }

    private static Post Reply(DiscussionThread thread, Post parent, string author = "helper", bool canModerate = false)
    {
        var reply = thread.Reply(parent, author, author, "A reply", canModerate, Now.AddMinutes(1));
        Assert.True(reply.IsSuccess, reply.Error?.Message);
        return reply.Value!;
    }

    [Fact]
    public void Starting_a_thread_creates_the_opening_post_and_raises_an_event()
    {
        var (thread, opening) = Question();

        Assert.Equal("How deep should I squat?", thread.Title);
        Assert.True(opening.IsOpeningPost);
        Assert.Equal(0, opening.Depth);
        Assert.Equal(thread.Id, opening.ThreadId);
        Assert.IsType<DiscussionThreadStarted>(Assert.Single(thread.Events));
    }

    [Theory]
    [InlineData("", "Body")]
    [InlineData("Title", "   ")]
    public void Empty_titles_and_bodies_are_rejected(string title, string body)
    {
        var started = DiscussionThread.Start(ProgramId, ThreadKind.General, title, body, "asker", "Asker", Now);

        Assert.Equal(ErrorKind.Invalid, started.Error?.Kind);
    }

    [Fact]
    public void Replies_nest_and_record_who_was_answered()
    {
        var (thread, opening) = Question();

        var first = Reply(thread, opening);
        var second = Reply(thread, first, author: "asker");

        Assert.Equal(first.Id, second.ParentPostId);
        Assert.Equal(2, second.Depth);
        Assert.Equal(Now.AddMinutes(1), thread.LastActivityAt);

        var posted = thread.Events.OfType<DiscussionReplyPosted>().Last();
        Assert.Equal("helper", posted.ParentAuthorId);
    }

    [Fact]
    public void Replies_stop_at_the_maximum_depth()
    {
        var (thread, parent) = Question();

        for (var depth = 1; depth <= DiscussionThread.MaxDepth; depth++)
        {
            parent = Reply(thread, parent);
        }

        var tooDeep = thread.Reply(parent, "helper", "helper", "One more", false, Now);

        Assert.Equal(ErrorKind.Invalid, tooDeep.Error?.Kind);
    }

    [Fact]
    public void Locked_threads_take_replies_from_moderators_only()
    {
        var (thread, opening) = Question();
        thread.SetLocked(true, Now);

        Assert.Equal(ErrorKind.Conflict, thread.Reply(opening, "helper", "helper", "Hi", canModerate: false, Now).Error?.Kind);
        Assert.True(thread.Reply(opening, "trainer", "trainer", "Closing note", canModerate: true, Now).IsSuccess);
    }

    [Fact]
    public void Hidden_threads_and_removed_posts_take_no_replies()
    {
        var (thread, opening) = Question();
        var reply = Reply(thread, opening);
        reply.SetHidden(true, "trainer", Now);

        Assert.Equal(ErrorKind.Conflict, thread.Reply(reply, "x", "x", "Hi", false, Now).Error?.Kind);

        thread.SetHidden(true, Now);
        Assert.Equal(ErrorKind.Conflict, thread.Reply(opening, "x", "x", "Hi", true, Now).Error?.Kind);
    }

    [Fact]
    public void Replies_must_belong_to_the_thread()
    {
        var (thread, _) = Question();
        var (_, otherOpening) = Question();

        Assert.Equal(ErrorKind.NotFound, thread.Reply(otherOpening, "x", "x", "Hi", false, Now).Error?.Kind);
    }

    [Fact]
    public void Only_the_asker_or_a_moderator_marks_the_answer()
    {
        var (thread, opening) = Question();
        var reply = Reply(thread, opening);

        Assert.Equal(ErrorKind.Forbidden, thread.MarkAnswer(reply, "someone-else", canModerate: false).Error?.Kind);
        Assert.True(thread.MarkAnswer(reply, "asker", canModerate: false).IsSuccess);
        Assert.Equal(reply.Id, thread.AnswerPostId);
        Assert.Single(thread.Events.OfType<DiscussionAnswerMarked>());

        Assert.True(thread.MarkAnswer(null, "trainer", canModerate: true).IsSuccess);
        Assert.Null(thread.AnswerPostId);
    }

    [Fact]
    public void The_answer_is_a_reply_in_a_question_thread()
    {
        var (question, opening) = Question();
        Assert.Equal(ErrorKind.Invalid, question.MarkAnswer(opening, "asker", false).Error?.Kind);

        var (general, generalOpening) = DiscussionThread.Start(ProgramId, ThreadKind.General, "Hi", "Body", "asker", "Asker", Now).Value;
        var reply = Reply(general, generalOpening);
        Assert.Equal(ErrorKind.Invalid, general.MarkAnswer(reply, "asker", false).Error?.Kind);
    }

    [Fact]
    public void Removing_the_answer_post_clears_the_answer()
    {
        var (thread, opening) = Question();
        var reply = Reply(thread, opening);
        thread.MarkAnswer(reply, "asker", false);

        thread.PostRemoved(reply.Id);

        Assert.Null(thread.AnswerPostId);
    }
}

public class PostTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static Post OpeningPost() =>
        DiscussionThread.Start(Guid.CreateVersion7(), ThreadKind.General, "Title", "Original", "author", "Author", Now).Value.OpeningPost;

    [Fact]
    public void Only_the_author_edits_and_not_in_a_locked_thread()
    {
        var post = OpeningPost();

        Assert.Equal(ErrorKind.Forbidden, post.Edit("someone-else", "Changed", false, Now).Error?.Kind);
        Assert.Equal(ErrorKind.Conflict, post.Edit("author", "Changed", threadLocked: true, Now).Error?.Kind);

        Assert.True(post.Edit("author", "  Changed  ", false, Now).IsSuccess);
        Assert.Equal("Changed", post.Body);
        Assert.Equal(Now, post.EditedAt);
    }

    [Fact]
    public void Deleting_a_post_without_replies_removes_it()
    {
        var deleted = OpeningPost().Delete("author", hasReplies: false, Now);

        Assert.True(deleted.Value);
    }

    [Fact]
    public void Deleting_a_post_with_replies_leaves_an_empty_placeholder()
    {
        var post = OpeningPost();

        var deleted = post.Delete("author", hasReplies: true, Now);

        Assert.False(deleted.Value);
        Assert.True(post.IsDeleted);
        Assert.Equal("", post.Body);
    }

    [Fact]
    public void Only_the_author_deletes()
    {
        Assert.Equal(ErrorKind.Forbidden, OpeningPost().Delete("someone-else", false, Now).Error?.Kind);
    }

    [Fact]
    public void Authors_cannot_delete_a_post_a_moderator_hid()
    {
        var post = OpeningPost();
        post.SetHidden(true, "trainer", Now);

        Assert.Equal(ErrorKind.Conflict, post.Delete("author", hasReplies: false, Now).Error?.Kind);
    }
}

public class PostReportTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static Post Post() =>
        DiscussionThread.Start(Guid.CreateVersion7(), ThreadKind.General, "Title", "Body", "author", "Author", Now).Value.OpeningPost;

    [Fact]
    public void Authors_cannot_report_their_own_posts()
    {
        Assert.Equal(ErrorKind.Invalid, PostReport.File(Post(), Guid.CreateVersion7(), "author", ReportReason.Spam, null, Now).Error?.Kind);
    }

    [Fact]
    public void A_report_is_resolved_once()
    {
        var report = PostReport.File(Post(), Guid.CreateVersion7(), "reporter", ReportReason.Abuse, " rude ", Now).Value!;
        Assert.Equal("rude", report.Note);

        Assert.True(report.Resolve(ReportStatus.Dismissed, "admin", Now).IsSuccess);
        Assert.Equal(ErrorKind.Conflict, report.Resolve(ReportStatus.Actioned, "admin", Now).Error?.Kind);
    }
}
