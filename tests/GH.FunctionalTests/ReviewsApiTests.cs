using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Reviews.Contracts;
using GymHub.Modules.Reviews.Data;

namespace GH.FunctionalTests;

public class ReviewsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _trainerId = $"trainer-{Guid.NewGuid():N}";
    private readonly string _memberId = $"member-{Guid.NewGuid():N}";
    private readonly string _otherId = $"other-{Guid.NewGuid():N}";

    private HttpClient Trainer => factory.ClientFor(_trainerId, "trainer");
    private HttpClient Member => factory.ClientFor(_memberId);
    private HttpClient Other => factory.ClientFor(_otherId);
    private HttpClient Admin => factory.ClientFor($"admin-{Guid.NewGuid():N}", "admin");
    private HttpClient Anonymous => factory.ClientFor(null);

    private sealed record IdResponse(Guid Id);
    private sealed record Reviewer(string UserId, string DisplayName);
    private sealed record Reply(string Body, string TrainerName);
    private sealed record Review(Guid Id, Reviewer Author, int Stars, string? Body, bool IsHidden, Reply? Reply);
    private sealed record Mine(Review? Review, bool CanWrite, string? Reason);
    private sealed record Summary(decimal? Average, int Count, IReadOnlyList<int> StarCounts);
    private sealed record Page(Summary Summary, PagedResult<Review> Reviews);
    private sealed record Report(Guid Id, string Status, Guid ProgramId, string ProgramTitle, Review Review);
    private sealed record Rating(decimal? Average, int Count);
    private sealed record Card(Guid Id, Rating Rating);
    private sealed record Overview(Guid Id, Rating Rating);
    private sealed record Item(string Type, string Title, string Link);

    /// <summary>A published free program by the trainer, with the member enrolled.</summary>
    private async Task<Guid> ProgramWithMemberAsync()
    {
        var created = await Trainer.PostAsJsonAsync("/api/v1/programs/", new { title = $"Review Lab {Guid.NewGuid():N}", kind = "Ongoing" }, Json, Ct);
        var id = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        await Trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.PostAsync($"/api/v1/programs/{id}/publish", null, Ct)).StatusCode);
        await Enroll(Member, id);
        return id;
    }

    private static async Task Enroll(HttpClient client, Guid programId) =>
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/v1/subscriptions/programs/{programId}", null, Ct)).StatusCode);

    private static Task<HttpResponseMessage> WriteAsync(HttpClient client, Guid programId, int stars, string? body = null) =>
        client.PutAsJsonAsync($"/api/v1/reviews/programs/{programId}/mine", new { stars, body }, Json, Ct);

    private static async Task<Page> PageAsync(HttpClient client, Guid programId) =>
        (await client.GetFromJsonAsync<Page>($"/api/v1/reviews/programs/{programId}", Json, Ct))!;

    private static async Task<Mine> MineAsync(HttpClient client, Guid programId) =>
        (await client.GetFromJsonAsync<Mine>($"/api/v1/reviews/programs/{programId}/mine", Json, Ct))!;

    private async Task<Rating> CatalogRatingAsync(Guid programId)
    {
        await factory.DispatchOutboxAsync<ReviewsDbContext>();
        var overview = await Anonymous.GetFromJsonAsync<Overview>($"/api/v1/programs/{programId}", Json, Ct);
        return overview!.Rating;
    }

    [Fact]
    public async Task Members_with_access_write_edit_and_delete_one_review()
    {
        var programId = await ProgramWithMemberAsync();

        var mine = await MineAsync(Member, programId);
        Assert.True(mine.CanWrite);
        Assert.Null(mine.Review);

        var written = await WriteAsync(Member, programId, 4, "  Solid program. ");
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        var review = (await written.Content.ReadFromJsonAsync<Review>(Json, Ct))!;
        Assert.Equal("Solid program.", review.Body);

        // Writing again edits the same review.
        var edited = (await (await WriteAsync(Member, programId, 5)).Content.ReadFromJsonAsync<Review>(Json, Ct))!;
        Assert.Equal(review.Id, edited.Id);
        Assert.Null(edited.Body);

        var page = await PageAsync(Anonymous, programId);
        Assert.Equal(new decimal?(5), page.Summary.Average);
        Assert.Equal(new[] { 0, 0, 0, 0, 1 }, page.Summary.StarCounts);
        Assert.Equal(review.Id, Assert.Single(page.Reviews.Data).Id);

        Assert.Equal(HttpStatusCode.NoContent, (await Member.DeleteAsync($"/api/v1/reviews/programs/{programId}/mine", Ct)).StatusCode);
        Assert.Equal(0, (await PageAsync(Anonymous, programId)).Summary.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await Member.DeleteAsync($"/api/v1/reviews/programs/{programId}/mine", Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_members_with_access_can_review_and_never_the_trainer()
    {
        var programId = await ProgramWithMemberAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await WriteAsync(Other, programId, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await WriteAsync(Trainer, programId, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WriteAsync(Anonymous, programId, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await WriteAsync(Member, programId, 6)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await WriteAsync(Member, Guid.NewGuid(), 5)).StatusCode);

        var other = await MineAsync(Other, programId);
        Assert.False(other.CanWrite);
        Assert.Contains("access", other.Reason!);
    }

    [Fact]
    public async Task A_review_outlives_access_but_is_locked_until_it_returns()
    {
        var programId = await ProgramWithMemberAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(Member, programId, 3, "Fine")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Member.DeleteAsync($"/api/v1/subscriptions/programs/{programId}", Ct)).StatusCode);

        var mine = await MineAsync(Member, programId);
        Assert.False(mine.CanWrite);
        Assert.Equal(3, mine.Review!.Stars);
        Assert.Equal(1, (await PageAsync(Anonymous, programId)).Summary.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await WriteAsync(Member, programId, 1)).StatusCode);

        await Enroll(Member, programId);
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(Member, programId, 1)).StatusCode);
    }

    [Fact]
    public async Task Only_the_programs_trainer_replies()
    {
        var programId = await ProgramWithMemberAsync();
        var review = (await (await WriteAsync(Member, programId, 2, "Too hard")).Content.ReadFromJsonAsync<Review>(Json, Ct))!;
        var replyUrl = $"/api/v1/reviews/{review.Id}/reply";

        Assert.Equal(HttpStatusCode.Forbidden, (await Member.PutAsJsonAsync(replyUrl, new { body = "Agreed" }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Admin.PutAsJsonAsync(replyUrl, new { body = "Hi" }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Trainer.PutAsJsonAsync(replyUrl, new { body = "" }, Json, Ct)).StatusCode);

        var replied = await Trainer.PutAsJsonAsync(replyUrl, new { body = "Try the scaled version in week 1." }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, replied.StatusCode);

        var shown = Assert.Single((await PageAsync(Anonymous, programId)).Reviews.Data);
        Assert.Equal("Try the scaled version in week 1.", shown.Reply!.Body);

        Assert.Equal(HttpStatusCode.NoContent, (await Trainer.DeleteAsync(replyUrl, Ct)).StatusCode);
        Assert.Null(Assert.Single((await PageAsync(Anonymous, programId)).Reviews.Data).Reply);
    }

    [Fact]
    public async Task Admins_hide_reported_reviews_which_leave_the_rating()
    {
        var programId = await ProgramWithMemberAsync();
        await Enroll(Other, programId);
        var spam = (await (await WriteAsync(Member, programId, 1, "Buy my program instead")).Content.ReadFromJsonAsync<Review>(Json, Ct))!;
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(Other, programId, 5)).StatusCode);

        Assert.Equal(new Rating(3, 2), await CatalogRatingAsync(programId));

        var reportUrl = $"/api/v1/reviews/{spam.Id}/reports";
        Assert.Equal(HttpStatusCode.BadRequest, (await Member.PostAsJsonAsync(reportUrl, new { reason = "Spam" }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Other.PostAsJsonAsync(reportUrl, new { reason = "Spam", note = "advert" }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Other.PostAsJsonAsync(reportUrl, new { reason = "Spam" }, Json, Ct)).StatusCode);

        // While it's reported, the author can't delete it (and its reports with it).
        var mineUrl = $"/api/v1/reviews/programs/{programId}/mine";
        Assert.Equal(HttpStatusCode.Conflict, (await Member.DeleteAsync(mineUrl, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Member.GetAsync("/api/v1/reviews/reports/", Ct)).StatusCode);
        var open = (await Admin.GetFromJsonAsync<PagedResult<Report>>("/api/v1/reviews/reports/?pageSize=50", Json, Ct))!.Data;
        var report = Assert.Single(open, r => r.Review.Id == spam.Id);
        Assert.StartsWith("Review Lab", report.ProgramTitle);

        Assert.Equal(HttpStatusCode.NoContent, (await Admin.PostAsync($"/api/v1/reviews/reports/{report.Id}/action", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PostAsync($"/api/v1/reviews/reports/{report.Id}/dismiss", null, Ct)).StatusCode);

        var page = await PageAsync(Anonymous, programId);
        Assert.Equal(5, Assert.Single(page.Reviews.Data).Stars);
        Assert.True((await MineAsync(Member, programId)).Review!.IsHidden);
        Assert.Equal(new Rating(5, 1), await CatalogRatingAsync(programId));

        var cards = await Anonymous.GetFromJsonAsync<PagedResult<Card>>("/api/v1/programs/?pageSize=50", Json, Ct);
        Assert.Equal(new Rating(5, 1), cards!.Data.Single(c => c.Id == programId).Rating);

        // Hidden now: deleting and rewriting it visibly isn't possible.
        Assert.Equal(HttpStatusCode.Conflict, (await Member.DeleteAsync(mineUrl, Ct)).StatusCode);

        // An admin can undo the hide; it counts again.
        var showUrl = $"/api/v1/reviews/reports/reviews/{spam.Id}/show";
        Assert.Equal(HttpStatusCode.Forbidden, (await Member.PostAsync(showUrl, null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Admin.PostAsync(showUrl, null, Ct)).StatusCode);
        Assert.Equal(2, (await PageAsync(Anonymous, programId)).Reviews.Data.Count);
        Assert.Equal(new Rating(3, 2), await CatalogRatingAsync(programId));
    }

    [Fact]
    public async Task Stale_rating_events_never_overwrite_newer_ones()
    {
        var programId = await ProgramWithMemberAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(Member, programId, 4)).StatusCode);
        Assert.Equal(new Rating(4, 1), await CatalogRatingAsync(programId));

        // Version 1 was the empty rating created before the first review; replaying it changes nothing.
        await factory.HandleAsync<GymHub.Modules.Programs.ProgramRatingHandler, ProgramRatingChanged>(new ProgramRatingChanged(programId, 0, 0, 1));

        Assert.Equal(new Rating(4, 1), await CatalogRatingAsync(programId));
    }

    [Fact]
    public async Task The_trainer_is_notified_of_a_new_review_once()
    {
        var programId = await ProgramWithMemberAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(Member, programId, 5, "Love it")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(Member, programId, 4, "Still good")).StatusCode);
        await factory.DispatchOutboxAsync<ReviewsDbContext>();

        // Deleting and writing it again is still the same member's review of this program.
        Assert.Equal(HttpStatusCode.NoContent, (await Member.DeleteAsync($"/api/v1/reviews/programs/{programId}/mine", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(Member, programId, 3, "Changed my mind")).StatusCode);
        await factory.DispatchOutboxAsync<ReviewsDbContext>();

        var inbox = (await Trainer.GetFromJsonAsync<PagedResult<Item>>("/api/v1/notifications/?pageSize=50", Json, Ct))!.Data;
        var item = Assert.Single(inbox, i => i.Type == "ReviewPosted");
        Assert.EndsWith("#reviews", item.Link);
        Assert.Contains("reviewed", item.Title);
    }
}
