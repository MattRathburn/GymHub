using System.Text.Json;
using EventBus.Abstractions;
using EventBus.Events;
using GymHub.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GymHub.BuildingBlocks.Outbox;

/// <summary>
/// Polls every module's outbox and hands each event to its in-process handlers, which are registered as keyed
/// <see cref="IIntegrationEventHandler"/> services exactly as EventBus's AddSubscription does. Delivery is
/// at-least-once, so handlers must be idempotent. Assumes a single API instance.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IEnumerable<ModuleDbContextRegistration> contexts,
    IOptions<EventBusSubscriptionInfo> subscriptionInfo,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const int MaxAttempts = 10;
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);

        do
        {
            foreach (var registration in contexts)
            {
                try
                {
                    await DispatchBatchAsync(registration.ContextType, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Outbox dispatch failed for {Context}", registration.ContextType.Name);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Dispatches one batch from one module's outbox. Public so tests can drive it without waiting.</summary>
    public async Task<int> DispatchBatchAsync(Type contextType, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(contextType);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null
                && m.Attempts < MaxAttempts
                && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await HandleAsync(scope.ServiceProvider, message);
                message.ProcessedAt = now;
                message.Error = null;
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.Error = ex.Message;
                message.NextAttemptAt = now.AddSeconds(Math.Pow(2, message.Attempts));
                logger.LogWarning(ex, "Handling outbox message {Id} ({Type}) failed, attempt {Attempt}", message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return messages.Count;
    }

    private async Task HandleAsync(IServiceProvider services, OutboxMessage message)
    {
        // Events nobody subscribes to are simply marked processed.
        if (!subscriptionInfo.Value.EventTypes.TryGetValue(message.Type, out var eventType))
        {
            return;
        }

        var @event = (IntegrationEvent)JsonSerializer.Deserialize(message.Content, eventType, OutboxMessage.SerializerOptions)!;

        foreach (var handler in services.GetKeyedServices<IIntegrationEventHandler>(eventType))
        {
            await handler.Handle(@event);
        }
    }
}
