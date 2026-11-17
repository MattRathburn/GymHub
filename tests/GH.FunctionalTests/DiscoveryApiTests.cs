using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Programs;
using GymHub.Modules.Reviews.Contracts;

namespace GH.FunctionalTests;

public class DiscoveryApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Anonymous => factory.ClientFor(null);

    private sealed record IdResponse(Guid Id);
    private sealed record Price(string Model, long? AmountMinor);
    private sealed record Rating(decimal? Average, int Count);
    private sealed record Card(Guid Id, string Title, Price Price, Rating Rating);
    private sealed record TrainerCard(string UserId, string DisplayName, int FollowerCount, int ProgramCount, Rating Rating);

    private static object Pricing(string model, long amount) => new { model, amountMinor = amount, currency = "USD", interval = model == "Recurring" ? "Monthly" : null };

    /// <summary>A trainer with a profile, ready to sell.</summary>
    private async Task<(string Id, HttpClient Client)> TrainerAsync(string name, string bio = "Strength coach", params string[] specialties)
    {
        var id = $"trainer-{Guid.NewGuid():N}";
        var client = factory.ClientFor(id, "trainer");
        var saved = await client.PutAsJsonAsync("/api/v1/trainers/me/profile", new { displayName = name, bio, specialties, links = Array.Empty<string>() }, Json, Ct);
        Assert.True(saved.IsSuccessStatusCode);
        await factory.MarkPayoutsReadyAsync(id);
        return (id, client);
    }

    private static async Task<Guid> ProgramAsync(HttpClient trainer, string title, string? summary = null, object? pricing = null, bool publish = true)
    {
        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title, summary, kind = "Ongoing" }, Json, Ct);
        var id = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        await trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);

        if (pricing is not null)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await trainer.PutAsJsonAsync($"/api/v1/programs/{id}/pricing", pricing, Json, Ct)).StatusCode);
        }

        if (publish)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await trainer.PostAsync($"/api/v1/programs/{id}/publish", null, Ct)).StatusCode);
        }

        return id;
    }

    private Task RateAsync(Guid programId, int count, int sum) =>
        factory.HandleAsync<ProgramRatingHandler, ProgramRatingChanged>(new ProgramRatingChanged(programId, count, sum, 1));

    private async Task<PagedResult<Card>> BrowseAsync(string query) =>
        (await Anonymous.GetFromJsonAsync<PagedResult<Card>>($"/api/v1/programs/?{query}", Json, Ct))!;

    private static string Token() => Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task Search_matches_title_summary_and_trainer_name()
    {
        var token = Token();
        var (_, coach) = await TrainerAsync($"Coach {token}");
        var (_, other) = await TrainerAsync("Someone Else");

        var byTitle = await ProgramAsync(other, $"Squats {token}");
        var bySummary = await ProgramAsync(other, "Plain title", summary: $"Built for {token} lifters");
        var byTrainer = await ProgramAsync(coach, "Another plain title");
        await ProgramAsync(other, "Unrelated");
        await ProgramAsync(coach, $"Draft {token}", publish: false);

        var found = await BrowseAsync($"search={token}&pageSize=50");

        Assert.Equal(3, found.Count);
        Assert.Equal(new HashSet<Guid> { byTitle, bySummary, byTrainer }, found.Data.Select(c => c.Id).ToHashSet());
    }

    [Fact]
    public async Task Pricing_type_filters_and_price_sorts()
    {
        var token = Token();
        var (_, trainer) = await TrainerAsync("Price Tester");
        var free = await ProgramAsync(trainer, $"Free {token}");
        var cheap = await ProgramAsync(trainer, $"Cheap {token}", pricing: Pricing("OneTime", 900));
        var monthly = await ProgramAsync(trainer, $"Monthly {token}", pricing: Pricing("Recurring", 1999));
        var pricey = await ProgramAsync(trainer, $"Pricey {token}", pricing: Pricing("OneTime", 4900));

        var oneTime = await BrowseAsync($"search={token}&pricing=OneTime");
        Assert.Equal(new HashSet<Guid> { cheap, pricey }, oneTime.Data.Select(c => c.Id).ToHashSet());
        Assert.Equal(monthly, Assert.Single((await BrowseAsync($"search={token}&pricing=Recurring")).Data).Id);
        Assert.Equal(free, Assert.Single((await BrowseAsync($"search={token}&pricing=Free")).Data).Id);

        Assert.Equal([free, cheap, monthly, pricey], (await BrowseAsync($"search={token}&sort=PriceLow")).Data.Select(c => c.Id));
        Assert.Equal([pricey, monthly, cheap, free], (await BrowseAsync($"search={token}&sort=PriceHigh")).Data.Select(c => c.Id));
    }

    [Fact]
    public async Task Price_sorts_compare_yearly_plans_per_month_and_group_currencies()
    {
        var token = Token();
        var (_, trainer) = await TrainerAsync("Interval Tester");
        var monthly = await ProgramAsync(trainer, $"Monthly {token}", pricing: Pricing("Recurring", 1999));
        var yearly = await ProgramAsync(trainer, $"Yearly {token}", pricing: new { model = "Recurring", amountMinor = 12000, currency = "USD", interval = "Yearly" });
        var euros = await ProgramAsync(trainer, $"Euros {token}", pricing: new { model = "OneTime", amountMinor = 100, currency = "EUR" });

        // 12000 a year is 1000 a month, cheaper than 1999 a month. EUR sorts as its own group, before USD.
        Assert.Equal([euros, yearly, monthly], (await BrowseAsync($"search={token}&sort=PriceLow")).Data.Select(c => c.Id));
        Assert.Equal([euros, monthly, yearly], (await BrowseAsync($"search={token}&sort=PriceHigh")).Data.Select(c => c.Id));
    }

    [Fact]
    public async Task Search_wildcards_match_themselves()
    {
        var token = Token();
        var (_, trainer) = await TrainerAsync("Wildcard Tester");
        var percent = await ProgramAsync(trainer, $"5% {token}");
        await ProgramAsync(trainer, $"50 {token}");

        Assert.Equal(percent, Assert.Single((await BrowseAsync($"search={Uri.EscapeDataString($"5% {token}")}")).Data).Id);
        Assert.Empty((await BrowseAsync($"search={Uri.EscapeDataString($"_ {token}_")}")).Data);
    }

    [Fact]
    public async Task Top_rated_weighs_review_counts_and_puts_unrated_last()
    {
        var token = Token();
        var (_, trainer) = await TrainerAsync("Rating Tester");
        var unrated = await ProgramAsync(trainer, $"Unrated {token}");
        var oneFiveStar = await ProgramAsync(trainer, $"One five {token}");
        var established = await ProgramAsync(trainer, $"Established {token}");
        var mediocre = await ProgramAsync(trainer, $"Mediocre {token}");

        await RateAsync(oneFiveStar, 1, 5);
        await RateAsync(established, 50, 240);
        await RateAsync(mediocre, 10, 25);

        var ranked = (await BrowseAsync($"search={token}&sort=TopRated")).Data.Select(c => c.Id).ToList();

        Assert.Equal([established, oneFiveStar, mediocre, unrated], ranked);
    }

    [Fact]
    public async Task Pages_through_results()
    {
        var token = Token();
        var (_, trainer) = await TrainerAsync("Paging Tester");

        for (var i = 0; i < 5; i++)
        {
            await ProgramAsync(trainer, $"Page {token} {i}");
        }

        var first = await BrowseAsync($"search={token}&pageSize=2&pageIndex=0");
        var third = await BrowseAsync($"search={token}&pageSize=2&pageIndex=2");

        Assert.Equal(5, first.Count);
        Assert.Equal(2, first.Data.Count);
        Assert.Single(third.Data);
    }

    [Fact]
    public async Task The_directory_lists_trainers_who_publish_and_searches_them()
    {
        var token = Token();
        var (publishingId, publishing) = await TrainerAsync($"Pat {token}", "Kettlebells and conditioning", "Mobility");
        var (_, draftsOnly) = await TrainerAsync($"Dana {token}");
        var specialist = await TrainerAsync("Riley Moves", $"Coach of {token}", "Yoga");

        var first = await ProgramAsync(publishing, "Bells 1");
        await ProgramAsync(publishing, "Bells 2");
        await ProgramAsync(draftsOnly, "Not yet", publish: false);
        await ProgramAsync(specialist.Client, "Flow");
        await RateAsync(first, 4, 18);

        Assert.Equal(HttpStatusCode.NoContent, (await factory.ClientFor($"fan-{token}").PutAsync($"/api/v1/trainers/{publishingId}/follow", null, Ct)).StatusCode);

        var found = (await Anonymous.GetFromJsonAsync<PagedResult<TrainerCard>>($"/api/v1/trainers/?search={token}", Json, Ct))!;

        Assert.Equal(2, found.Count);
        var pat = found.Data[0];
        Assert.Equal(publishingId, pat.UserId);
        Assert.Equal((1, 2), (pat.FollowerCount, pat.ProgramCount));
        Assert.Equal(new Rating(4.5m, 4), pat.Rating);
        Assert.Equal(specialist.Id, found.Data[1].UserId);
        Assert.Equal(new Rating(null, 0), found.Data[1].Rating);

        var bySpecialty = (await Anonymous.GetFromJsonAsync<PagedResult<TrainerCard>>("/api/v1/trainers/?search=yoga&pageSize=50", Json, Ct))!;
        Assert.Contains(bySpecialty.Data, t => t.UserId == specialist.Id);
        Assert.DoesNotContain(bySpecialty.Data, t => t.UserId == publishingId);
    }
}
