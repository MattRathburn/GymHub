using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Subscriptions.Data;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace GH.FunctionalTests;

public class DiscussionsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _trainerId = $"trainer-{Guid.NewGuid():N}";
    private readonly string _memberId = $"member-{Guid.NewGuid():N}";
    private readonly HttpClient _admin = factory.ClientFor("admin-1", "admin");

    private HttpClient Trainer => factory.ClientFor(_trainerId, "trainer");
    private HttpClient Member => factory.ClientFor(_memberId);

    private sealed record IdResponse(Guid Id);
    private sealed record WeekResponse(Guid WeekId);
    private sealed record Started(Guid Id, Guid OpeningPostId);
    private sealed record Replied(Guid Id, Guid ThreadId, int Depth);
    private sealed record Author(string UserId, string DisplayName, bool IsTrainer);
    private sealed record ThreadSummary(Guid Id, string Title, Author Author, int ReplyCount, bool IsPinned, bool IsHidden, bool IsAnswered);
    private sealed record Board(Guid ProgramId, bool CanPost, bool CanModerate, PagedResult<ThreadSummary> Threads);
    private sealed record Post(Guid Id, Guid? ParentPostId, int Depth, Author? Author, string? Body, bool IsDeleted, bool IsHidden, bool IsMine, bool IsAnswer);
    private sealed record Thread(Guid Id, string Title, bool IsLocked, Guid? AnswerPostId, bool CanReply, bool CanModerate, List<Post> Posts);
    private sealed record Report(Guid Id, Guid PostId, string Reason, string Status);

    /// <summary>A published free program owned by <see cref="_trainerId"/>, with <see cref="_memberId"/> enrolled.</summary>
    private async Task<Guid> BoardAsync()
    {
        var trainer = Trainer;
        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = $"Program {Guid.NewGuid():N}", kind = "Ongoing" }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;

        var week = await trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, week.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await trainer.PostAsync($"/api/v1/programs/{id}/publish", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Member.PutAsync($"/api/v1/subscriptions/programs/{id}", null, Ct)).StatusCode);
        return id;
    }

    private static async Task<Started> StartAsync(HttpClient client, Guid programId, string kind = "Question", string title = "How deep?")
    {
        var response = await client.PostAsJsonAsync($"/api/v1/discussions/programs/{programId}/threads", new { kind, title, body = "Asking for a friend." }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Started>(Json, Ct))!;
    }

    private static async Task<Replied> ReplyAsync(HttpClient client, Guid threadId, Guid parentPostId, string body = "Below parallel.")
    {
        var response = await client.PostAsJsonAsync($"/api/v1/discussions/threads/{threadId}/posts", new { parentPostId, body }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Replied>(Json, Ct))!;
    }

    private static async Task<Thread> ThreadAsync(HttpClient client, Guid threadId) =>
        (await client.GetFromJsonAsync<Thread>($"/api/v1/discussions/threads/{threadId}", Json, Ct))!;

    [Fact]
    public async Task Only_subscribers_the_trainer_and_admins_can_see_the_board()
    {
        var programId = await BoardAsync();
        var outsider = factory.ClientFor($"user-{Guid.NewGuid():N}");
        var otherTrainer = factory.ClientFor($"trainer-{Guid.NewGuid():N}", "trainer");

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientFor(null).GetAsync($"/api/v1/discussions/programs/{programId}/threads", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/discussions/programs/{programId}/threads", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTrainer.GetAsync($"/api/v1/discussions/programs/{programId}/threads", Ct)).StatusCode);

        var asMember = await Member.GetFromJsonAsync<Board>($"/api/v1/discussions/programs/{programId}/threads", Json, Ct);
        Assert.True(asMember!.CanPost);
        Assert.False(asMember.CanModerate);

        Assert.True((await Trainer.GetFromJsonAsync<Board>($"/api/v1/discussions/programs/{programId}/threads", Json, Ct))!.CanModerate);
        Assert.True((await _admin.GetFromJsonAsync<Board>($"/api/v1/discussions/programs/{programId}/threads", Json, Ct))!.CanModerate);
    }

    [Fact]
    public async Task Unsubscribing_removes_access()
    {
        var programId = await BoardAsync();
        var thread = await StartAsync(Member, programId);

        Assert.Equal(HttpStatusCode.NoContent, (await Member.DeleteAsync($"/api/v1/subscriptions/programs/{programId}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Member.GetAsync($"/api/v1/discussions/threads/{thread.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Replies_nest_and_show_the_trainer_badge_and_profile_names()
    {
        var programId = await BoardAsync();
        var saved = await Member.PutAsJsonAsync("/api/v1/trainers/me/member-profile", new { displayName = "Lifting Lou" }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var thread = await StartAsync(Member, programId);
        var trainerReply = await ReplyAsync(Trainer, thread.Id, thread.OpeningPostId);
        var memberReply = await ReplyAsync(Member, thread.Id, trainerReply.Id, "Thanks!");

        Assert.Equal(1, trainerReply.Depth);
        Assert.Equal(2, memberReply.Depth);

        var loaded = await ThreadAsync(Member, thread.Id);
        Assert.Equal(3, loaded.Posts.Count);
        Assert.Equal("Lifting Lou", loaded.Posts[0].Author!.DisplayName);
        Assert.True(loaded.Posts[1].Author!.IsTrainer);
        Assert.Equal(trainerReply.Id, loaded.Posts[2].ParentPostId);
        Assert.True(loaded.Posts[2].IsMine);

        var board = await Member.GetFromJsonAsync<Board>($"/api/v1/discussions/programs/{programId}/threads", Json, Ct);
        Assert.Equal(2, Assert.Single(board!.Threads.Data).ReplyCount);
    }

    [Fact]
    public async Task Archived_programs_keep_a_read_only_board()
    {
        var programId = await BoardAsync();
        var thread = await StartAsync(Member, programId);
        var answer = await ReplyAsync(Trainer, thread.Id, thread.OpeningPostId);

        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/programs/{programId}/archive", null, Ct)).StatusCode);

        var loaded = await ThreadAsync(Member, thread.Id);
        Assert.False(loaded.CanReply);

        var reply = await Member.PostAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/posts", new { parentPostId = thread.OpeningPostId, body = "Hello?" }, Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, reply.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Trainer.DeleteAsync($"/api/v1/discussions/posts/{answer.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Member.PutAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/answer", new { postId = answer.Id }, Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Moderation_is_for_the_programs_trainer_and_admins()
    {
        var programId = await BoardAsync();
        var thread = await StartAsync(Member, programId);
        var otherTrainer = factory.ClientFor($"trainer-{Guid.NewGuid():N}", "trainer");

        Assert.Equal(HttpStatusCode.Forbidden, (await Member.PatchAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/moderation", new { pinned = true }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTrainer.PatchAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/moderation", new { pinned = true }, Json, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PatchAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/moderation", new { pinned = true, locked = true }, Json, Ct)).StatusCode);

        var locked = await Member.PostAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/posts", new { parentPostId = thread.OpeningPostId, body = "Hi" }, Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PatchAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/moderation", new { hidden = true }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Member.GetAsync($"/api/v1/discussions/threads/{thread.Id}", Ct)).StatusCode);

        var board = await Member.GetFromJsonAsync<Board>($"/api/v1/discussions/programs/{programId}/threads", Json, Ct);
        Assert.Empty(board!.Threads.Data);
    }

    [Fact]
    public async Task The_asker_marks_the_answer_and_deleting_keeps_the_tree()
    {
        var programId = await BoardAsync();
        var thread = await StartAsync(Member, programId);
        var answer = await ReplyAsync(Trainer, thread.Id, thread.OpeningPostId);
        var followUp = await ReplyAsync(Member, thread.Id, answer.Id, "Got it.");

        Assert.Equal(HttpStatusCode.NoContent, (await Member.PutAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/answer", new { postId = answer.Id }, Json, Ct)).StatusCode);
        Assert.Equal(answer.Id, (await ThreadAsync(Member, thread.Id)).AnswerPostId);

        // The trainer's answer has a reply, so deleting it leaves a placeholder.
        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.DeleteAsync($"/api/v1/discussions/posts/{answer.Id}", Ct)).StatusCode);
        // The follow-up has none, so it goes outright.
        Assert.Equal(HttpStatusCode.NoContent, (await Member.DeleteAsync($"/api/v1/discussions/posts/{followUp.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Member.DeleteAsync($"/api/v1/discussions/posts/{answer.Id}", Ct)).StatusCode);

        var loaded = await ThreadAsync(Member, thread.Id);
        Assert.Equal(2, loaded.Posts.Count);
        var placeholder = loaded.Posts.Single(p => p.Id == answer.Id);
        Assert.True(placeholder.IsDeleted);
        Assert.Null(placeholder.Body);
        Assert.Null(placeholder.Author);
    }

    [Fact]
    public async Task Admins_act_on_reports_by_hiding_the_post()
    {
        var programId = await BoardAsync();
        var thread = await StartAsync(Trainer, programId, "General", "Week 1 check-in");
        var rude = await ReplyAsync(Member, thread.Id, thread.OpeningPostId, "Something rude");

        // A second subscriber reports it twice; only one open report results.
        var reporterId = $"member-{Guid.NewGuid():N}";
        var reporter = factory.ClientFor(reporterId);
        Assert.Equal(HttpStatusCode.NoContent, (await reporter.PutAsync($"/api/v1/subscriptions/programs/{programId}", null, Ct)).StatusCode);

        for (var i = 0; i < 2; i++)
        {
            var reported = await reporter.PostAsJsonAsync($"/api/v1/discussions/posts/{rude.Id}/reports", new { reason = "Abuse", note = "Not okay" }, Json, Ct);
            Assert.Equal(HttpStatusCode.NoContent, reported.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await Member.GetAsync("/api/v1/discussions/reports", Ct)).StatusCode);

        // The author can't delete it away (and its reports with it) while it's under review.
        Assert.Equal(HttpStatusCode.Conflict, (await Member.DeleteAsync($"/api/v1/discussions/posts/{rude.Id}", Ct)).StatusCode);

        var open = await _admin.GetFromJsonAsync<PagedResult<Report>>("/api/v1/discussions/reports?pageSize=50", Json, Ct);
        var report = Assert.Single(open!.Data, r => r.PostId == rude.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync($"/api/v1/discussions/reports/{report.Id}/action", null, Ct)).StatusCode);

        var asMember = (await ThreadAsync(reporter, thread.Id)).Posts.Single(p => p.Id == rude.Id);
        Assert.True(asMember.IsHidden);
        Assert.Null(asMember.Body);

        var asTrainer = (await ThreadAsync(Trainer, thread.Id)).Posts.Single(p => p.Id == rude.Id);
        Assert.Equal("Something rude", asTrainer.Body);

        var stillOpen = await _admin.GetFromJsonAsync<PagedResult<Report>>("/api/v1/discussions/reports?pageSize=50", Json, Ct);
        Assert.DoesNotContain(stillOpen!.Data, r => r.PostId == rude.Id);

        // Hidden now, so still not deletable by its author.
        Assert.Equal(HttpStatusCode.Conflict, (await Member.DeleteAsync($"/api/v1/discussions/posts/{rude.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Posting_writes_discussion_events_to_the_outbox()
    {
        var programId = await BoardAsync();
        var thread = await StartAsync(Member, programId);
        await ReplyAsync(Trainer, thread.Id, thread.OpeningPostId);

        // Content is jsonb, so filter in memory.
        var messages = await factory.WithDbAsync<DiscussionsDbContext, List<GymHub.BuildingBlocks.Outbox.OutboxMessage>>(db =>
            db.OutboxMessages.AsNoTracking().ToListAsync(Ct));
        var types = messages.Where(m => m.Content.Contains(thread.Id.ToString(), StringComparison.Ordinal)).Select(m => m.Type).ToList();

        Assert.Contains("DiscussionThreadStarted", types);
        Assert.Contains("DiscussionReplyPosted", types);
    }

    [Fact]
    public async Task Hub_clients_on_a_board_hear_about_new_posts_and_outsiders_cannot_join()
    {
        var programId = await BoardAsync();

        await using var subscriber = await HubAsync(_memberId);
        var changed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscriber.On<Guid, Guid>("threadChanged", (_, threadId) => changed.TrySetResult(threadId));
        await subscriber.InvokeAsync("JoinBoard", programId, Ct);

        var thread = await StartAsync(Trainer, programId, "General", "Welcome");

        Assert.Equal(thread.Id, await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct));

        await using var outsider = await HubAsync($"user-{Guid.NewGuid():N}");
        var refused = await Assert.ThrowsAsync<HubException>(() => outsider.InvokeAsync("JoinBoard", programId, Ct));
        Assert.Contains("subscribers", refused.Message);
    }

    [Fact]
    public async Task Hidden_threads_cannot_be_joined_by_subscribers()
    {
        var programId = await BoardAsync();
        var thread = await StartAsync(Member, programId);
        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PatchAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/moderation", new { hidden = true }, Json, Ct)).StatusCode);

        await using var subscriber = await HubAsync(_memberId);
        await Assert.ThrowsAsync<HubException>(() => subscriber.InvokeAsync("JoinThread", thread.Id, Ct));

        await using var trainer = await HubAsync(_trainerId, "trainer");
        await trainer.InvokeAsync("JoinThread", thread.Id, Ct);
    }

    [Fact]
    public async Task Leaving_a_program_stops_its_live_updates()
    {
        var programId = await BoardAsync();

        await using var member = await HubAsync(_memberId);
        var memberHeard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        member.On<Guid, Guid>("threadChanged", (_, _) => memberHeard.TrySetResult());
        await member.InvokeAsync("JoinBoard", programId, Ct);

        await using var trainer = await HubAsync(_trainerId, "trainer");
        var trainerHeard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        trainer.On<Guid, Guid>("threadChanged", (_, _) => trainerHeard.TrySetResult());
        await trainer.InvokeAsync("JoinBoard", programId, Ct);

        Assert.Equal(HttpStatusCode.NoContent, (await Member.DeleteAsync($"/api/v1/subscriptions/programs/{programId}", Ct)).StatusCode);
        while (await factory.DispatchOutboxAsync<SubscriptionsDbContext>() > 0)
        {
        }

        await StartAsync(Trainer, programId, "General", "Members only");

        // Both are sent in one group broadcast; once the trainer has it, the member would have too.
        await trainerHeard.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        Assert.False(memberHeard.Task.IsCompleted);
    }

    private async Task<HubConnection> HubAsync(string userId, string roles = "")
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/api/v1/discussions/hub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[TestAuthHandler.UserHeader] = userId;
                options.Headers[TestAuthHandler.RolesHeader] = roles;
            })
            .Build();

        await connection.StartAsync(Ct);
        return connection;
    }
}
