using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Programs.Data;
using Microsoft.EntityFrameworkCore;

namespace GH.FunctionalTests;

public class ProgramsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _anonymous = factory.ClientFor(null);
    private readonly HttpClient _trainer = factory.ClientFor("trainer-1", "trainer");
    private readonly HttpClient _otherTrainer = factory.ClientFor("trainer-2", "trainer");
    private readonly HttpClient _member = factory.ClientFor($"member-{Guid.NewGuid():N}");

    private sealed record IdResponse(Guid Id);
    private sealed record WeekResponse(Guid WeekId, int Number);
    private sealed record Card(Guid Id, string Title);
    private sealed record Week(Guid Id, int Number, string Title, bool IsReleased);
    private sealed record Content(Guid Id, string Title, List<Week> Weeks);
    private sealed record Overview(Guid Id, List<Week> Weeks);

    /// <summary>Creates and publishes a program with one released week and one future week.</summary>
    private async Task<Guid> PublishedProgramAsync(object? pricing = null)
    {
        var created = await _trainer.PostAsJsonAsync("/api/v1/programs/", new
        {
            title = $"Program {Guid.NewGuid():N}",
            summary = "Get strong",
            kind = "Ongoing"
        }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;

        var released = await _trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks", new { title = "Week 1", releasesOn = Today.AddDays(-1) }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
        var weekId = (await released.Content.ReadFromJsonAsync<WeekResponse>(Json, Ct))!.WeekId;
        await _trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks", new { title = "Week 2", releasesOn = Today.AddDays(6) }, Json, Ct);

        var day = await _trainer.PutAsJsonAsync($"/api/v1/programs/{id}/weeks/{weekId}/days/1", new { title = "Lower", isRestDay = false }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, day.StatusCode);

        var workout = await _trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks/{weekId}/days/1/workouts", new
        {
            title = "Squat day",
            exercises = new[] { new { name = "Back squat", sets = new[] { new { reps = 5, weightKg = 100 } } } }
        }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, workout.StatusCode);

        if (pricing is not null)
        {
            var priced = await _trainer.PutAsJsonAsync($"/api/v1/programs/{id}/pricing", pricing, Json, Ct);
            Assert.Equal(HttpStatusCode.NoContent, priced.StatusCode);
        }

        var published = await _trainer.PostAsync($"/api/v1/programs/{id}/publish", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        return id;
    }

    [Fact]
    public async Task Anonymous_users_can_browse_and_read_overviews_but_not_content()
    {
        var id = await PublishedProgramAsync();

        var page = await _anonymous.GetFromJsonAsync<PagedResult<Card>>("/api/v1/programs/?pageSize=50", Json, Ct);
        Assert.Contains(page!.Data, c => c.Id == id);

        var overview = await _anonymous.GetFromJsonAsync<Overview>($"/api/v1/programs/{id}", Json, Ct);
        Assert.Equal(2, overview!.Weeks.Count);

        var content = await _anonymous.GetAsync($"/api/v1/programs/{id}/content", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, content.StatusCode);
    }

    [Fact]
    public async Task Members_see_released_weeks_only_after_enrolling()
    {
        var id = await PublishedProgramAsync();

        var before = await _member.GetAsync($"/api/v1/programs/{id}/content", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        var enrolled = await _member.PutAsync($"/api/v1/subscriptions/programs/{id}", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, enrolled.StatusCode);

        var content = await _member.GetFromJsonAsync<Content>($"/api/v1/programs/{id}/content", Json, Ct);
        var week = Assert.Single(content!.Weeks);
        Assert.Equal("Week 1", week.Title);

        var left = await _member.DeleteAsync($"/api/v1/subscriptions/programs/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _member.GetAsync($"/api/v1/programs/{id}/content", Ct)).StatusCode);
    }

    [Fact]
    public async Task Paid_programs_cannot_be_joined_for_free()
    {
        await factory.MarkPayoutsReadyAsync("trainer-1");
        var id = await PublishedProgramAsync(new { model = "Recurring", amountMinor = 1999, currency = "USD", interval = "Monthly" });

        var enrolled = await _member.PutAsync($"/api/v1/subscriptions/programs/{id}", null, Ct);

        Assert.Equal(HttpStatusCode.PaymentRequired, enrolled.StatusCode);
    }

    [Fact]
    public async Task Only_the_owning_trainer_can_edit()
    {
        var id = await PublishedProgramAsync();

        var asOtherTrainer = await _otherTrainer.PutAsJsonAsync($"/api/v1/programs/{id}", new { title = "Mine now", kind = "Ongoing" }, Json, Ct);
        var asMember = await _member.PutAsJsonAsync($"/api/v1/programs/{id}", new { title = "Mine now", kind = "Ongoing" }, Json, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, asOtherTrainer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asMember.StatusCode);
    }

    [Fact]
    public async Task Invalid_requests_return_validation_problems()
    {
        var response = await _trainer.PostAsJsonAsync("/api/v1/programs/", new { title = "", kind = "Ongoing" }, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Title", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Publishing_writes_ProgramPublished_to_the_outbox_and_dispatches_it()
    {
        var id = await PublishedProgramAsync();

        var before = await PublishedMessageAsync(id);
        Assert.NotNull(before);
        Assert.Null(before.ProcessedAt);

        await factory.DispatchOutboxAsync<ProgramsDbContext>();

        Assert.Contains(factory.PublishedEvents.Received, e => e.ProgramId == id);
        Assert.NotNull((await PublishedMessageAsync(id))?.ProcessedAt);
    }

    private Task<GymHub.BuildingBlocks.Outbox.OutboxMessage?> PublishedMessageAsync(Guid programId) =>
        factory.WithDbAsync<ProgramsDbContext, GymHub.BuildingBlocks.Outbox.OutboxMessage?>(async db =>
            (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == "ProgramPublished").ToListAsync(Ct))
                .SingleOrDefault(m => m.Content.Contains(programId.ToString(), StringComparison.Ordinal)));
}
