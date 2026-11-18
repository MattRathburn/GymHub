using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Privacy;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Accounts;
using GymHub.Modules.Accounts.Data;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Domain;
using GymHub.Modules.Notifications.Handlers;
using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.TrainingLog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GH.FunctionalTests;

public class AccountApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly object Confirmation = new { confirmation = "Delete my account" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record IdResponse(Guid Id);
    private sealed record Started(Guid Id, Guid OpeningPostId);
    private sealed record Pending(DateTime RequestedAt, DateTime ScheduledFor);
    private sealed record Status(Pending? Deletion, List<string> DeletionBlockers);
    private sealed record Scheduled(DateTime ScheduledFor);

    private static string NewUserId() => $"user-{Guid.NewGuid():N}";

    private static async Task<Status> StatusAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Status>("/api/v1/account/", Json, Ct))!;

    private static Task<HttpResponseMessage> ScheduleAsync(HttpClient client, object? body = null) =>
        client.PostAsJsonAsync("/api/v1/account/deletion", body ?? Confirmation, Json, Ct);

    private async Task<(string Id, HttpClient Client, Guid ProgramId)> TrainerWithProgramAsync()
    {
        var id = NewUserId();
        var trainer = factory.ClientFor(id, "trainer");
        await trainer.PutAsJsonAsync("/api/v1/trainers/me/profile", new { displayName = "Coach", bio = (string?)null, specialties = Array.Empty<string>(), links = Array.Empty<string>() }, Json, Ct);

        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = "Account test program", kind = "Ongoing" }, Json, Ct);
        var programId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        await trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await trainer.PostAsync($"/api/v1/programs/{programId}/publish", null, Ct)).StatusCode);

        return (id, trainer, programId);
    }

    /// <summary>Makes the pending deletion due, as if the grace period had passed, and runs the job once.</summary>
    private async Task<int> RunJobAfterGracePeriodAsync(string userId)
    {
        await factory.WithDbAsync<AccountsDbContext, int>(db => db.Deletions
            .Where(d => d.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ScheduledFor, DateTime.UtcNow.AddMinutes(-1)), Ct));

        return await factory.Services.GetRequiredService<AccountDeletionJob>().RunOnceAsync(Ct);
    }

    [Fact]
    public async Task Scheduling_needs_the_phrase_waits_a_week_and_can_be_cancelled()
    {
        var member = factory.ClientFor(NewUserId());

        Assert.Equal(HttpStatusCode.BadRequest, (await ScheduleAsync(member, new { confirmation = "yes" })).StatusCode);
        Assert.Null((await StatusAsync(member)).Deletion);

        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(member)).StatusCode);
        var pending = (await StatusAsync(member)).Deletion!;
        Assert.Equal(TimeSpan.FromDays(7), pending.ScheduledFor - pending.RequestedAt);

        // Asking twice keeps the first date.
        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(member)).StatusCode);
        Assert.Equal(pending, (await StatusAsync(member)).Deletion);

        // Not due yet: the job leaves it alone.
        await factory.Services.GetRequiredService<AccountDeletionJob>().RunOnceAsync(Ct);
        Assert.NotNull((await StatusAsync(member)).Deletion);

        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync("/api/v1/account/deletion", Ct)).StatusCode);
        Assert.Null((await StatusAsync(member)).Deletion);
        Assert.Equal(HttpStatusCode.NotFound, (await member.DeleteAsync("/api/v1/account/deletion", Ct)).StatusCode);
    }

    [Fact]
    public async Task Published_programs_block_deletion_until_they_are_archived()
    {
        var (trainerId, trainer, programId) = await TrainerWithProgramAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await ScheduleAsync(trainer)).StatusCode);
        Assert.Contains("published program", Assert.Single((await StatusAsync(trainer)).DeletionBlockers));

        Assert.Equal(HttpStatusCode.NoContent, (await trainer.PostAsync($"/api/v1/programs/{programId}/archive", null, Ct)).StatusCode);
        Assert.Empty((await StatusAsync(trainer)).DeletionBlockers);
        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(trainer)).StatusCode);

        Assert.Equal(1, await RunJobAfterGracePeriodAsync(trainerId));
        Assert.Contains(trainerId, factory.IdentityAdmin.DeletedUsers);

        // The archived program stays for the members who have it.
        Assert.True(await factory.WithDbAsync<ProgramsDbContext, bool>(db => db.Programs.AnyAsync(p => p.Id == programId, Ct)));
    }

    [Fact]
    public async Task Draft_programs_and_their_media_are_deleted_and_archived_ones_stay()
    {
        var (trainerId, trainer, archivedId) = await TrainerWithProgramAsync();
        await trainer.PostAsync($"/api/v1/programs/{archivedId}/archive", null, Ct);

        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = "Unfinished draft", kind = "Ongoing" }, Json, Ct);
        var draftId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        var storage = (FakeObjectStorage)factory.Services.GetRequiredService<IObjectStorage>();
        var draftCover = $"{TrainingProgram.MediaKeyPrefixFor(draftId)}cover.jpg";
        var archivedCover = $"{TrainingProgram.MediaKeyPrefixFor(archivedId)}cover.jpg";
        storage.Objects[draftCover] = "image/jpeg";
        storage.Objects[archivedCover] = "image/jpeg";

        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(trainer)).StatusCode);
        Assert.Equal(1, await RunJobAfterGracePeriodAsync(trainerId));

        Assert.False(await factory.WithDbAsync<ProgramsDbContext, bool>(db => db.Programs.AnyAsync(p => p.Id == draftId, Ct)));
        Assert.True(await factory.WithDbAsync<ProgramsDbContext, bool>(db => db.Programs.AnyAsync(p => p.Id == archivedId, Ct)));
        Assert.False(storage.Objects.ContainsKey(draftCover));
        Assert.True(storage.Objects.ContainsKey(archivedCover));
    }

    [Fact]
    public async Task Erasing_enrollments_announces_that_access_ended()
    {
        var (_, _, programId) = await TrainerWithProgramAsync();
        var userId = NewUserId();
        var member = factory.ClientFor(userId);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PutAsync($"/api/v1/subscriptions/programs/{programId}", null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(member)).StatusCode);
        Assert.Equal(1, await RunJobAfterGracePeriodAsync(userId));

        // The outbox carries it to Discussions, which drops the member's open board connections.
        var cancelled = (await factory.WithDbAsync<SubscriptionsDbContext, List<string>>(db => db.OutboxMessages
                .Where(m => m.Type.Contains(nameof(EnrollmentCancelled)))
                .Select(m => m.Content)
                .ToListAsync(Ct)))
            .Where(c => c.Contains(userId, StringComparison.Ordinal))
            .ToList();
        Assert.Contains(programId.ToString(), Assert.Single(cancelled), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_requests_at_once_both_get_the_same_date()
    {
        var member = factory.ClientFor(NewUserId());

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ScheduleAsync(member)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var dates = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<Scheduled>(Json, Ct)));
        Assert.Single(dates.Select(d => d!.ScheduledFor).Distinct());
    }

    [Fact]
    public void Every_export_section_has_its_own_name()
    {
        using var scope = factory.Services.CreateScope();
        var sections = scope.ServiceProvider.GetServices<IPersonalDataSource>().Select(s => s.Section).ToList();

        Assert.Equal(sections.Count, sections.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task A_blocker_that_appears_during_the_grace_period_holds_the_deletion()
    {
        var (trainerId, trainer, programId) = await TrainerWithProgramAsync();
        await trainer.PostAsync($"/api/v1/programs/{programId}/archive", null, Ct);
        await ScheduleAsync(trainer);

        var second = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = "Another one", kind = "Ongoing" }, Json, Ct);
        var secondId = (await second.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        await trainer.PostAsJsonAsync($"/api/v1/programs/{secondId}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
        await trainer.PostAsync($"/api/v1/programs/{secondId}/publish", null, Ct);

        Assert.Equal(0, await RunJobAfterGracePeriodAsync(trainerId));
        Assert.DoesNotContain(trainerId, factory.IdentityAdmin.DeletedUsers);
        Assert.NotNull((await StatusAsync(trainer)).Deletion);
    }

    [Fact]
    public async Task The_job_erases_private_data_and_anonymizes_posts_and_reviews()
    {
        var (trainerId, _, programId) = await TrainerWithProgramAsync();
        var userId = NewUserId();
        var member = factory.ClientWithEmail(userId, $"{userId}@example.com");

        await member.PutAsJsonAsync("/api/v1/trainers/me/member-profile", new { displayName = "Sam Member" }, Json, Ct);
        await member.PutAsync($"/api/v1/trainers/{trainerId}/follow", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PutAsync($"/api/v1/subscriptions/programs/{programId}", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.PutAsJsonAsync($"/api/v1/reviews/programs/{programId}/mine", new { stars = 4, body = "Solid." }, Json, Ct)).StatusCode);
        var thread = (await (await member.PostAsJsonAsync($"/api/v1/discussions/programs/{programId}/threads",
            new { kind = "Question", title = "Form check", body = "How deep should I squat?" }, Json, Ct)).Content.ReadFromJsonAsync<Started>(Json, Ct))!;
        Assert.Equal(HttpStatusCode.Created, (await member.PostAsJsonAsync("/api/v1/training/sessions",
            new { performedOn = Today, exercises = new[] { new { name = "Back squat", sets = new[] { new { reps = 5, weightKg = 100m } } } } }, Json, Ct)).StatusCode);
        await member.GetAsync("/api/v1/notifications/unread-count", Ct);

        var export = await member.GetAsync("/api/v1/account/export", Ct);
        Assert.Equal("gymhub-data.json", export.Content.Headers.ContentDisposition?.FileNameStar ?? export.Content.Headers.ContentDisposition?.FileName);
        var exported = JsonDocument.Parse(await export.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("data");
        Assert.Equal("Sam Member", exported.GetProperty("profile").GetProperty("memberProfile").GetProperty("displayName").GetString());
        Assert.Equal(1, exported.GetProperty("trainingLog").GetArrayLength());
        Assert.Equal("How deep should I squat?", exported.GetProperty("discussions").GetProperty("posts")[0].GetProperty("body").GetString());
        Assert.Equal($"{userId}@example.com", exported.GetProperty("notifications").GetProperty("email").GetProperty("email").GetString());

        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(member)).StatusCode);
        Assert.Equal(1, await RunJobAfterGracePeriodAsync(userId));

        Assert.Contains(userId, factory.IdentityAdmin.DeletedUsers);
        Assert.False(await factory.WithDbAsync<TrainersDbContext, bool>(db => db.MemberProfiles.AnyAsync(p => p.UserId == userId, Ct)));
        Assert.False(await factory.WithDbAsync<TrainersDbContext, bool>(db => db.Follows.AnyAsync(f => f.UserId == userId, Ct)));
        Assert.False(await factory.WithDbAsync<SubscriptionsDbContext, bool>(db => db.Enrollments.AnyAsync(e => e.UserId == userId, Ct)));
        Assert.False(await factory.WithDbAsync<TrainingLogDbContext, bool>(db => db.Sessions.AnyAsync(s => s.UserId == userId, Ct)));
        Assert.False(await factory.WithDbAsync<NotificationsDbContext, bool>(db => db.Recipients.AnyAsync(r => r.UserId == userId, Ct)));

        // Posts and reviews stay, word for word, under "Deleted user".
        var post = await factory.WithDbAsync<DiscussionsDbContext, (string, string)>(async db =>
            await db.Posts.Where(p => p.Id == thread.OpeningPostId).Select(p => new { p.AuthorName, p.Body }).SingleAsync(Ct) is var x ? (x.AuthorName, x.Body) : default);
        Assert.Equal((DeletedUser.DisplayName, "How deep should I squat?"), post);
        var review = await factory.WithDbAsync<ReviewsDbContext, (string, string?)>(async db =>
            await db.Reviews.Where(r => r.AuthorId == userId).Select(r => new { r.AuthorName, r.Body }).SingleAsync(Ct) is var x ? (x.AuthorName, x.Body) : default);
        Assert.Equal((DeletedUser.DisplayName, "Solid."), review);

        // Running again finds nothing to do, and the deleted account's still-valid token is refused everywhere, so a
        // still-open tab can't store anything again (the bell saves the email address).
        Assert.Equal(0, await factory.Services.GetRequiredService<AccountDeletionJob>().RunOnceAsync(Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, (await ScheduleAsync(member)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/v1/notifications/unread-count", Ct)).StatusCode);
        Assert.False(await factory.WithDbAsync<NotificationsDbContext, bool>(db => db.Recipients.AnyAsync(r => r.UserId == userId, Ct)));
    }

    [Fact]
    public async Task Erasure_closes_open_checkouts_and_deletes_the_Stripe_customer()
    {
        var (trainerId, trainer, _) = await TrainerWithProgramAsync();
        await factory.MarkPayoutsReadyAsync(trainerId);

        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = "Paid program", kind = "Ongoing" }, Json, Ct);
        var paidId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        await trainer.PostAsJsonAsync($"/api/v1/programs/{paidId}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
        await trainer.PutAsJsonAsync($"/api/v1/programs/{paidId}/pricing", new { model = "OneTime", amountMinor = 4900, currency = "USD" }, Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await trainer.PostAsync($"/api/v1/programs/{paidId}/publish", null, Ct)).StatusCode);

        var userId = NewUserId();
        var member = factory.ClientWithEmail(userId, $"{userId}@example.com");
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsync($"/api/v1/billing/programs/{paidId}/checkout", null, Ct)).StatusCode);

        var (sessionId, customerId) = await factory.WithDbAsync<BillingDbContext, (string, string)>(async db =>
            (await db.Purchases.Where(p => p.UserId == userId).Select(p => p.CheckoutSessionId!).SingleAsync(Ct),
             await db.Customers.Where(c => c.UserId == userId).Select(c => c.StripeCustomerId).SingleAsync(Ct)));

        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(member)).StatusCode);
        Assert.Equal(1, await RunJobAfterGracePeriodAsync(userId));

        Assert.Contains(sessionId, factory.Stripe.ExpiredCheckouts);
        Assert.Contains(customerId, factory.Stripe.DeletedCustomers);
        Assert.False(await factory.WithDbAsync<BillingDbContext, bool>(db => db.Customers.AnyAsync(c => c.UserId == userId, Ct)));
    }

    [Fact]
    public async Task Other_peoples_notifications_lose_the_deleted_users_name()
    {
        var (trainerId, _, programId) = await TrainerWithProgramAsync();
        var userId = NewUserId();
        var member = factory.ClientFor(userId);
        await member.PutAsJsonAsync("/api/v1/trainers/me/member-profile", new { displayName = "Robin Leaving" }, Json, Ct);

        await factory.HandleAsync<FreeEnrollmentHandler, EnrollmentStarted>(new EnrollmentStarted(userId, programId));

        Task<List<string>> TrainerTitlesAsync() => factory.WithDbAsync<NotificationsDbContext, List<string>>(db =>
            db.Notifications.Where(n => n.UserId == trainerId && n.Type == NotificationType.NewMember).Select(n => n.Title).ToListAsync(Ct));

        Assert.Contains(await TrainerTitlesAsync(), t => t.StartsWith("Robin Leaving joined", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(member)).StatusCode);
        Assert.Equal(1, await RunJobAfterGracePeriodAsync(userId));

        var titles = await TrainerTitlesAsync();
        Assert.DoesNotContain(titles, t => t.Contains("Robin Leaving", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.StartsWith($"{DeletedUser.DisplayName} joined", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Late_events_create_no_notifications_for_a_deleted_account()
    {
        var (_, _, programId) = await TrainerWithProgramAsync();
        var userId = NewUserId();
        var member = factory.ClientFor(userId);
        Assert.Equal(HttpStatusCode.OK, (await ScheduleAsync(member)).StatusCode);
        Assert.Equal(1, await RunJobAfterGracePeriodAsync(userId));

        await factory.HandleAsync<PaymentFailedHandler, SubscriptionPaymentFailed>(
            new SubscriptionPaymentFailed(userId, programId, Guid.NewGuid()));

        Assert.False(await factory.WithDbAsync<NotificationsDbContext, bool>(db => db.Notifications.AnyAsync(n => n.UserId == userId, Ct)));
    }
}
