using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Outbox;
using GymHub.Modules.Subscriptions.Data;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace GH.FunctionalTests;

/// <summary>A throwaway Redis for the backplane tests.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => _redis.GetConnectionString();

    public async ValueTask InitializeAsync() => await _redis.StartAsync();

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();
}

/// <summary>
/// Two API instances sharing Postgres and Redis: SignalR messages and board-access revocations reach clients
/// connected to the other instance.
/// </summary>
public class RedisBackplaneTests(ApiFactory factory, RedisFixture redis) : IClassFixture<ApiFactory>, IClassFixture<RedisFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _trainerId = $"trainer-{Guid.NewGuid():N}";
    private readonly string _memberId = $"member-{Guid.NewGuid():N}";

    private sealed record IdResponse(Guid Id);

    private WebApplicationFactory<Program> Instance() =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:redis", redis.ConnectionString));

    private static HttpClient ClientFor(WebApplicationFactory<Program> instance, string userId, string roles = "")
    {
        var client = instance.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }

    private static async Task<HubConnection> HubAsync(WebApplicationFactory<Program> instance, string userId, string roles = "")
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(instance.Server.BaseAddress, "/api/v1/discussions/hub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => instance.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[TestAuthHandler.UserHeader] = userId;
                options.Headers[TestAuthHandler.RolesHeader] = roles;
            })
            .Build();

        await connection.StartAsync(Ct);
        return connection;
    }

    /// <summary>A published free program with the member enrolled.</summary>
    private async Task<Guid> BoardAsync(HttpClient trainer, HttpClient member)
    {
        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = $"Program {Guid.NewGuid():N}", kind = "Ongoing" }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;

        var week = await trainer.PostAsJsonAsync($"/api/v1/programs/{id}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, week.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await trainer.PostAsync($"/api/v1/programs/{id}/publish", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PutAsync($"/api/v1/subscriptions/programs/{id}", null, Ct)).StatusCode);
        return id;
    }

    private static async Task StartThreadAsync(HttpClient client, Guid programId, string title)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/discussions/programs/{programId}/threads", new { kind = "General", title, body = "Hello." }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<TaskCompletionSource> ListenAsync(HubConnection connection, Guid programId)
    {
        var heard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<Guid, Guid>("threadChanged", (_, _) => heard.TrySetResult());
        await connection.InvokeAsync("JoinBoard", programId, Ct);
        return heard;
    }

    [Fact]
    public async Task A_post_on_one_instance_reaches_clients_on_the_other()
    {
        var first = Instance();
        var second = Instance();
        var programId = await BoardAsync(ClientFor(second, _trainerId, "trainer"), ClientFor(second, _memberId));

        await using var member = await HubAsync(first, _memberId);
        var heard = await ListenAsync(member, programId);

        await StartThreadAsync(ClientFor(second, _trainerId, "trainer"), programId, "From the other instance");

        await heard.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task Leaving_on_one_instance_takes_the_member_off_the_board_on_the_other()
    {
        var first = Instance();
        var second = Instance();
        var trainerApi = ClientFor(second, _trainerId, "trainer");
        var memberApi = ClientFor(second, _memberId);
        var programId = await BoardAsync(trainerApi, memberApi);

        await using var member = await HubAsync(first, _memberId);
        var memberHeard = await ListenAsync(member, programId);
        await using var trainer = await HubAsync(first, _trainerId, "trainer");
        var trainerHeard = await ListenAsync(trainer, programId);

        // The second instance handles the cancelled enrollment and tells every instance through Redis.
        Assert.Equal(HttpStatusCode.NoContent, (await memberApi.DeleteAsync($"/api/v1/subscriptions/programs/{programId}", Ct)).StatusCode);
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(second.Services);
        while (await dispatcher.DispatchBatchAsync(typeof(SubscriptionsDbContext), Ct) > 0)
        {
        }

        // Pub/sub delivery is asynchronous; give the first instance a moment to act on it.
        await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        await StartThreadAsync(trainerApi, programId, "Members only");

        await trainerHeard.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        Assert.False(memberHeard.Task.IsCompleted);
    }
}
