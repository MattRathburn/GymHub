using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Delivery;
using GymHub.Modules.Notifications.Domain;
using GymHub.Modules.Notifications.Handlers;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Domain;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GH.FunctionalTests;

public class NotificationsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _trainerId = $"trainer-{Guid.NewGuid():N}";
    private readonly string _memberId = $"member-{Guid.NewGuid():N}";

    private HttpClient Trainer => factory.ClientFor(_trainerId, "trainer");
    private HttpClient Member => factory.ClientFor(_memberId);

    private sealed record IdResponse(Guid Id);
    private sealed record Started(Guid Id, Guid OpeningPostId);
    private sealed record Replied(Guid Id);
    private sealed record Item(Guid Id, string Type, string Title, string Link, bool IsRead);
    private sealed record Unread(int Count);
    private sealed record Preference(string Type, bool InApp, bool Email, bool EmailLocked);

    /// <summary>A published free program by the trainer, with the member enrolled.</summary>
    private async Task<Guid> ProgramWithMemberAsync()
    {
        var created = await Trainer.PostAsJsonAsync("/api/v1/programs/", new { title = $"Squat Lab {Guid.NewGuid():N}", kind = "Ongoing" }, Json, Ct);
        var id = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        await Trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/programs/{id}/publish", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Member.PutAsync($"/api/v1/subscriptions/programs/{id}", null, Ct)).StatusCode);
        return id;
    }

    private static async Task<IReadOnlyList<Item>> InboxAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PagedResult<Item>>("/api/v1/notifications/?pageSize=50", Json, Ct))!.Data;

    private static async Task<int> UnreadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Unread>("/api/v1/notifications/unread-count", Json, Ct))!.Count;

    private Task<List<EmailMessage>> EmailsToAsync(string address) =>
        factory.WithDbAsync<NotificationsDbContext, List<EmailMessage>>(db => db.Emails.AsNoTracking().Where(e => e.ToAddress == address).ToListAsync(Ct));

    [Fact]
    public async Task A_released_week_notifies_members_once_and_emails_those_with_an_address()
    {
        var programId = await ProgramWithMemberAsync();
        var address = $"{_memberId}@example.test";
        var member = factory.ClientWithEmail(_memberId, address);
        Assert.Equal(0, await UnreadAsync(member));

        var released = new ProgramWeekReleased(programId, Guid.NewGuid(), 2, "Heavy singles", Today);
        await factory.HandleAsync<WeekReleasedHandler, ProgramWeekReleased>(released);
        await factory.HandleAsync<WeekReleasedHandler, ProgramWeekReleased>(released);

        var item = Assert.Single(await InboxAsync(member), n => n.Type == "WeekReleased");
        Assert.StartsWith("Week 2 of Squat Lab", item.Title);
        Assert.Equal($"/programs/{programId}", item.Link);
        Assert.Single(await EmailsToAsync(address));

        // The trainer isn't a member, so they hear nothing.
        Assert.DoesNotContain(await InboxAsync(Trainer), n => n.Type == "WeekReleased");

        // Sending: once ok; a failing address is retried later instead of lost.
        await factory.Services.GetRequiredService<EmailDispatcher>().SendDueAsync(Ct);
        Assert.Contains(factory.Email.Sent, e => e.To == address && e.Subject.Contains("Week 2", StringComparison.Ordinal));
        Assert.Equal(EmailStatus.Sent, Assert.Single(await EmailsToAsync(address)).Status);
    }

    [Fact]
    public async Task Failed_emails_are_retried_later()
    {
        var address = $"{_memberId}@down.test";
        factory.Email.Failing[address] = true;
        await UnreadAsync(factory.ClientWithEmail(_memberId, address));

        await factory.HandleAsync<TrainerApprovedHandler, GymHub.Modules.Trainers.Contracts.TrainerApproved>(
            new(_memberId, Guid.NewGuid(), "Sam"));
        await factory.Services.GetRequiredService<EmailDispatcher>().SendDueAsync(Ct);

        var email = Assert.Single(await EmailsToAsync(address));
        Assert.Equal(EmailStatus.Pending, email.Status);
        Assert.Equal(1, email.Attempts);
        Assert.True(email.NextAttemptAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Discussion_activity_notifies_the_trainer_and_the_person_replied_to_but_never_the_actor()
    {
        var programId = await ProgramWithMemberAsync();

        var thread = (await (await Member.PostAsJsonAsync($"/api/v1/discussions/programs/{programId}/threads",
            new { kind = "Question", title = "How deep?", body = "Asking." }, Json, Ct)).Content.ReadFromJsonAsync<Started>(Json, Ct))!;
        var reply = await Trainer.PostAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/posts",
            new { parentPostId = thread.OpeningPostId, body = "Below parallel." }, Json, Ct);
        var trainerReply = (await reply.Content.ReadFromJsonAsync<Replied>(Json, Ct))!;
        await Member.PostAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/posts",
            new { parentPostId = thread.OpeningPostId, body = "Adding detail to my own question." }, Json, Ct);
        await Member.PutAsJsonAsync($"/api/v1/discussions/threads/{thread.Id}/answer", new { postId = trainerReply.Id }, Json, Ct);
        await factory.DispatchOutboxAsync<DiscussionsDbContext>();

        var trainerInbox = await InboxAsync(Trainer);
        Assert.Single(trainerInbox, n => n.Type == "ThreadStarted" && n.Link.EndsWith($"/discussion/{thread.Id}", StringComparison.Ordinal));
        Assert.Single(trainerInbox, n => n.Type == "AnswerMarked");

        // One reply from the trainer; the member's reply to their own post notifies nobody.
        Assert.Single(await InboxAsync(Member), n => n.Type == "PostReplied");
        Assert.DoesNotContain(trainerInbox, n => n.Type == "PostReplied");
    }

    [Fact]
    public async Task Followers_hear_when_a_trainer_publishes_and_the_trainer_hears_about_new_members()
    {
        await factory.WithDbAsync<TrainersDbContext, int>(db =>
        {
            db.Profiles.Add(TrainerProfile.Create(_trainerId, TrainerDetails.Create("Coach Kim", null, null, null), DateTime.UtcNow));
            db.Follows.Add(Follow.Start(_memberId, _trainerId, DateTime.UtcNow).Value!);
            return db.SaveChangesAsync(Ct);
        });

        var programId = await ProgramWithMemberAsync();
        await factory.DispatchOutboxAsync<ProgramsDbContext>();
        await factory.DispatchOutboxAsync<GymHub.Modules.Subscriptions.Data.SubscriptionsDbContext>();

        Assert.Single(await InboxAsync(Member), n => n.Type == "TrainerPublished" && n.Link == $"/programs/{programId}");
        Assert.Single(await InboxAsync(Trainer), n => n.Type == "NewMember" && n.Link == $"/programs/{programId}");
    }

    [Fact]
    public async Task Preferences_turn_types_off_but_billing_emails_stay_on()
    {
        var address = $"{_memberId}@example.test";
        var member = factory.ClientWithEmail(_memberId, address);
        await UnreadAsync(member);

        var defaults = await member.GetFromJsonAsync<List<Preference>>("/api/v1/notifications/preferences", Json, Ct);
        Assert.NotNull(defaults);
        Assert.True(defaults.Single(p => p.Type == "AccessEnded").EmailLocked);
        Assert.False(defaults.Single(p => p.Type == "PostReplied").Email);

        var locked = await member.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { preferences = new[] { new { type = "AccessEnded", inApp = false, email = false } } }, Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);

        var saved = await member.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { preferences = new[] { new { type = "AccessEnded", inApp = false, email = true }, new { type = "PostReplied", inApp = false, email = false } } },
            Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        await factory.HandleAsync<AccessEndedHandler, PaidAccessEnded>(new(_memberId, Guid.NewGuid(), Guid.NewGuid(), PaidAccessEndReason.Refunded));
        await factory.HandleAsync<ReplyPostedHandler, GymHub.Modules.Discussions.Contracts.DiscussionReplyPosted>(
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "someone", _memberId));

        Assert.Empty(await InboxAsync(member));
        Assert.Contains("refunded", Assert.Single(await EmailsToAsync(address)).TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Members_read_their_own_notifications_only()
    {
        for (var i = 0; i < 3; i++)
        {
            await factory.HandleAsync<TrainerApprovedHandler, GymHub.Modules.Trainers.Contracts.TrainerApproved>(new(_memberId, Guid.NewGuid(), "Sam"));
        }

        Assert.Equal(3, await UnreadAsync(Member));
        var first = (await InboxAsync(Member))[0];

        var stranger = factory.ClientFor($"member-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/v1/notifications/{first.Id}/read", null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Member.PostAsync($"/api/v1/notifications/{first.Id}/read", null, Ct)).StatusCode);
        Assert.Equal(2, await UnreadAsync(Member));
        var unreadOnly = await Member.GetFromJsonAsync<PagedResult<Item>>("/api/v1/notifications/?unreadOnly=true", Json, Ct);
        Assert.Equal(2, unreadOnly!.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await Member.PostAsync("/api/v1/notifications/read-all", null, Ct)).StatusCode);
        Assert.Equal(0, await UnreadAsync(Member));
    }

    [Fact]
    public async Task The_hub_pushes_unread_counts_to_the_right_user_only()
    {
        var otherId = $"member-{Guid.NewGuid():N}";
        await using var mine = await HubAsync(_memberId);
        await using var other = await HubAsync(otherId);

        var myCount = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        mine.On<int>("notificationsChanged", count => myCount.TrySetResult(count));
        var otherHeard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        other.On<int>("notificationsChanged", _ => otherHeard.TrySetResult());

        await factory.HandleAsync<TrainerApprovedHandler, GymHub.Modules.Trainers.Contracts.TrainerApproved>(new(_memberId, Guid.NewGuid(), "Sam"));

        Assert.Equal(1, await myCount.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        await Task.Delay(300, Ct);
        Assert.False(otherHeard.Task.IsCompleted);
    }

    private async Task<HubConnection> HubAsync(string userId)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/api/v1/notifications/hub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[TestAuthHandler.UserHeader] = userId;
                options.Headers[TestAuthHandler.RolesHeader] = "";
            })
            .Build();

        await connection.StartAsync(Ct);
        return connection;
    }
}
