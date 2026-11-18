using System.Collections.Concurrent;
using EventBus.Abstractions;
using EventBus.Events;
using GymHub.BuildingBlocks.Jobs;
using GymHub.BuildingBlocks.Outbox;
using GymHub.Modules.Programs.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GH.FunctionalTests;

/// <summary>Two API instances (two hosts) on one database: outbox claims and singleton job locks.</summary>
public class MultiInstanceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record ProbeEvent(int Number) : IntegrationEvent;

    /// <summary>Records each delivery, then waits for the test to open the gate, so a claim stays held.</summary>
    public sealed class ProbeHandler : IIntegrationEventHandler<ProbeEvent>
    {
        public static ConcurrentQueue<Guid> Delivered { get; } = new();

        public static TaskCompletionSource Gate { get; set; } = new();

        public async Task Handle(ProbeEvent @event)
        {
            Delivered.Enqueue(@event.Id);
            await Gate.Task;
        }
    }

    private WebApplicationFactory<Program> Instance() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddIntegrationEventHandler<ProbeEvent, ProbeHandler>()));

    private static Task<int> DispatchAsync(WebApplicationFactory<Program> instance) =>
        ActivatorUtilities.CreateInstance<OutboxDispatcher>(instance.Services).DispatchBatchAsync(typeof(ProgramsDbContext), CancellationToken.None);

    [Fact]
    public async Task Two_instances_dispatch_each_outbox_message_once()
    {
        var first = Instance();
        var second = Instance();
        ProbeHandler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ProbeHandler.Delivered.Clear();

        var events = Enumerable.Range(1, 5).Select(n => new ProbeEvent(n)).ToList();
        await factory.WithDbAsync<ProgramsDbContext, int>(db =>
        {
            events.ForEach(db.AddIntegrationEvent);
            return db.SaveChangesAsync(Ct);
        });

        // The first instance claims the batch and is stuck in the first handler.
        var firstPass = DispatchAsync(first);
        while (ProbeHandler.Delivered.IsEmpty)
        {
            await Task.Delay(20, Ct);
        }

        // The claim is already committed (another connection sees the lease), so no transaction is held while the
        // handler runs.
        var ids = events.Select(e => e.Id).ToList();
        var leasedUntil = await factory.WithDbAsync<ProgramsDbContext, List<DateTime?>>(db => db.OutboxMessages
            .Where(m => ids.Contains(m.Id))
            .Select(m => m.NextAttemptAt)
            .ToListAsync(Ct));
        Assert.All(leasedUntil, until => Assert.True(until > DateTime.UtcNow.AddMinutes(4)));

        // Meanwhile the second instance finds nothing it may take. (Had it taken a message, its handler would wait on
        // the closed gate too, so time out instead of hanging.)
        Assert.Equal(0, await DispatchAsync(second).WaitAsync(TimeSpan.FromSeconds(10), Ct));

        ProbeHandler.Gate.SetResult();
        Assert.Equal(5, await firstPass);
        Assert.Equal(0, await DispatchAsync(second));

        Assert.Equal(events.Select(e => e.Id).Order(), ProbeHandler.Delivered.Order());
    }

    [Fact]
    public async Task A_job_runs_on_one_instance_at_a_time()
    {
        var first = Instance().Services.GetRequiredService<ISingletonJobLock>();
        var second = Instance().Services.GetRequiredService<ISingletonJobLock>();
        bool? secondRanMeanwhile = null;

        var firstRan = await first.TryRunAsync("probe-job", async ct =>
            secondRanMeanwhile = await second.TryRunAsync("probe-job", _ => Task.CompletedTask, ct), Ct);

        Assert.True(firstRan);
        Assert.False(secondRanMeanwhile);

        // Released afterwards, and other jobs were never blocked.
        Assert.True(await second.TryRunAsync("probe-job", _ => Task.CompletedTask, Ct));
        Assert.True(await second.TryRunAsync("another-job", _ => Task.CompletedTask, Ct));
    }
}
