using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Trainers.Data;

namespace GH.FunctionalTests;

public class TrainersApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _admin = factory.ClientFor("admin-1", "admin");
    private readonly HttpClient _anonymous = factory.ClientFor(null);

    private sealed record Application(Guid Id, string UserId, string DisplayName, string Status, string? RejectionReason);
    private sealed record Profile(string UserId, string DisplayName, string? Bio, List<string> Specialties, Uri? AvatarUrl, int FollowerCount, bool IsFollowing);
    private sealed record Followed(string UserId, string DisplayName);
    private sealed record Upload(string ObjectKey, Uri UploadUrl);

    private static object ApplicationBody(string name = "Sam Strong") => new
    {
        displayName = name,
        bio = "Ten years coaching barbell sports.",
        specialties = new[] { "Powerlifting", "Mobility" },
        links = new[] { "https://example.com/sam" }
    };

    private static string NewUserId() => $"user-{Guid.NewGuid():N}";

    private async Task<Application> ApplyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/trainers/applications", ApplicationBody(), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Application>(Json, Ct))!;
    }

    /// <summary>A user who applied and was approved; returns their user id.</summary>
    private async Task<string> ApprovedTrainerAsync()
    {
        var userId = NewUserId();
        var application = await ApplyAsync(factory.ClientFor(userId));
        var approved = await _admin.PostAsync($"/api/v1/trainers/applications/{application.Id}/approve", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        return userId;
    }

    [Fact]
    public async Task Approving_an_application_creates_the_profile_and_grants_the_trainer_role()
    {
        var userId = NewUserId();
        var member = factory.ClientFor(userId);

        var application = await ApplyAsync(member);
        Assert.Equal("Pending", application.Status);

        var pending = await _admin.GetFromJsonAsync<PagedResult<Application>>("/api/v1/trainers/applications?pageSize=50", Json, Ct);
        Assert.Contains(pending!.Data, a => a.Id == application.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await _anonymous.GetAsync($"/api/v1/trainers/{userId}", Ct)).StatusCode);

        var approved = await _admin.PostAsync($"/api/v1/trainers/applications/{application.Id}/approve", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);

        var mine = await member.GetFromJsonAsync<Application>("/api/v1/trainers/applications/mine", Json, Ct);
        Assert.Equal("Approved", mine!.Status);

        var profile = await _anonymous.GetFromJsonAsync<Profile>($"/api/v1/trainers/{userId}", Json, Ct);
        Assert.Equal("Sam Strong", profile!.DisplayName);
        Assert.Equal(["Powerlifting", "Mobility"], profile.Specialties);

        // The Keycloak grant happens from the outbox.
        Assert.DoesNotContain(factory.IdentityAdmin.Grants, g => g.UserId == userId);
        await factory.DispatchOutboxAsync<TrainersDbContext>();
        Assert.Contains((userId, "trainer"), factory.IdentityAdmin.Grants);

        var again = await _admin.PostAsync($"/api/v1/trainers/applications/{application.Id}/approve", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Rejected_users_can_reapply_but_not_while_pending()
    {
        var member = factory.ClientFor(NewUserId());
        var application = await ApplyAsync(member);

        var duplicate = await member.PostAsJsonAsync("/api/v1/trainers/applications", ApplicationBody(), Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var rejected = await _admin.PostAsJsonAsync($"/api/v1/trainers/applications/{application.Id}/reject", new { reason = "Add certifications" }, Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);

        var mine = await member.GetFromJsonAsync<Application>("/api/v1/trainers/applications/mine", Json, Ct);
        Assert.Equal("Rejected", mine!.Status);
        Assert.Equal("Add certifications", mine.RejectionReason);

        await ApplyAsync(member);
    }

    [Fact]
    public async Task Only_admins_review_applications()
    {
        var member = factory.ClientFor(NewUserId());
        var application = await ApplyAsync(member);
        var trainer = factory.ClientFor(NewUserId(), "trainer");

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/trainers/applications", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await trainer.PostAsync($"/api/v1/trainers/applications/{application.Id}/approve", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PostAsJsonAsync("/api/v1/trainers/applications", ApplicationBody(), Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Invalid_applications_and_existing_trainers_are_refused()
    {
        var member = factory.ClientFor(NewUserId());
        var invalid = await member.PostAsJsonAsync("/api/v1/trainers/applications", new { displayName = "", links = new[] { "javascript:alert(1)" } }, Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var trainer = factory.ClientFor(NewUserId(), "trainer");
        var asTrainer = await trainer.PostAsJsonAsync("/api/v1/trainers/applications", ApplicationBody(), Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, asTrainer.StatusCode);
    }

    [Fact]
    public async Task Members_follow_and_unfollow_trainers()
    {
        var trainerId = await ApprovedTrainerAsync();
        var member = factory.ClientFor(NewUserId());

        Assert.Equal(HttpStatusCode.NoContent, (await member.PutAsync($"/api/v1/trainers/{trainerId}/follow", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PutAsync($"/api/v1/trainers/{trainerId}/follow", null, Ct)).StatusCode);

        var profile = await member.GetFromJsonAsync<Profile>($"/api/v1/trainers/{trainerId}", Json, Ct);
        Assert.Equal(1, profile!.FollowerCount);
        Assert.True(profile.IsFollowing);

        var following = await member.GetFromJsonAsync<List<Followed>>("/api/v1/trainers/following", Json, Ct);
        Assert.Equal(trainerId, Assert.Single(following!).UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync($"/api/v1/trainers/{trainerId}/follow", Ct)).StatusCode);
        Assert.Empty((await member.GetFromJsonAsync<List<Followed>>("/api/v1/trainers/following", Json, Ct))!);

        var self = factory.ClientFor(trainerId, "trainer");
        Assert.Equal(HttpStatusCode.BadRequest, (await self.PutAsync($"/api/v1/trainers/{trainerId}/follow", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PutAsync($"/api/v1/trainers/{NewUserId()}/follow", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Trainers_edit_their_profile_and_avatar()
    {
        // A trainer who never applied (like the seeded one) creates a profile by saving it.
        var trainerId = NewUserId();
        var trainer = factory.ClientFor(trainerId, "trainer");

        Assert.Equal(HttpStatusCode.NotFound, (await trainer.GetAsync("/api/v1/trainers/me/profile", Ct)).StatusCode);

        var saved = await trainer.PutAsJsonAsync("/api/v1/trainers/me/profile", ApplicationBody("Taylor"), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var upload = await (await trainer.PostAsJsonAsync("/api/v1/trainers/me/avatar/uploads", new { fileName = "me.png", contentType = "image/png" }, Json, Ct))
            .Content.ReadFromJsonAsync<Upload>(Json, Ct);
        Assert.StartsWith($"trainers/{trainerId}/avatar/", upload!.ObjectKey);

        var otherTrainer = factory.ClientFor(NewUserId(), "trainer");
        await otherTrainer.PutAsJsonAsync("/api/v1/trainers/me/profile", ApplicationBody("Other"), Json, Ct);
        var stolen = await otherTrainer.PutAsJsonAsync("/api/v1/trainers/me/avatar", new { objectKey = upload.ObjectKey }, Json, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, stolen.StatusCode);

        var attached = await trainer.PutAsJsonAsync("/api/v1/trainers/me/avatar", new { objectKey = upload.ObjectKey }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, attached.StatusCode);

        var profile = await _anonymous.GetFromJsonAsync<Profile>($"/api/v1/trainers/{trainerId}", Json, Ct);
        Assert.Equal("Taylor", profile!.DisplayName);
        Assert.NotNull(profile.AvatarUrl);

        var member = factory.ClientFor(NewUserId());
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync("/api/v1/trainers/me/profile", ApplicationBody(), Json, Ct)).StatusCode);
    }

    private sealed record MemberProfile(string UserId, string DisplayName, Uri? AvatarUrl);

    [Fact]
    public async Task Any_user_sets_up_a_member_profile_with_an_avatar()
    {
        var userId = NewUserId();
        var member = factory.ClientFor(userId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/v1/trainers/me/member-profile", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/v1/trainers/me/member-profile", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync("/api/v1/trainers/me/member-profile", new { displayName = "x" }, Json, Ct)).StatusCode);

        var saved = await member.PutAsJsonAsync("/api/v1/trainers/me/member-profile", new { displayName = "  Lou  " }, Json, Ct);
        Assert.Equal("Lou", (await saved.Content.ReadFromJsonAsync<MemberProfile>(Json, Ct))!.DisplayName);

        var upload = await (await member.PostAsJsonAsync("/api/v1/trainers/me/member-profile/avatar/uploads", new { fileName = "me.png", contentType = "image/png" }, Json, Ct))
            .Content.ReadFromJsonAsync<Upload>(Json, Ct);
        Assert.StartsWith($"members/{userId}/avatar/", upload!.ObjectKey);

        var attached = await member.PutAsJsonAsync("/api/v1/trainers/me/member-profile/avatar", new { objectKey = upload.ObjectKey }, Json, Ct);
        Assert.NotNull((await attached.Content.ReadFromJsonAsync<MemberProfile>(Json, Ct))!.AvatarUrl);
    }
}
