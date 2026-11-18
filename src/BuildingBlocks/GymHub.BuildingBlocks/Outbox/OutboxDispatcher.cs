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
/// at-least-once, so handlers must be idempotent. Instances lease the batches they claim, so several can run.
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

    /// <summary>
    /// How long a claimed batch is reserved for the instance that claimed it. Far longer than handlers take; if the
    /// instance dies meanwhile, the messages become due again when it runs out.
    /// </summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Dispatches one batch from one module's outbox. The batch is claimed in a short transaction (FOR UPDATE SKIP
    /// LOCKED, then NextAttemptAt moved a lease ahead), so other instances pass over it without a transaction being
    /// held while handlers make network calls. Each message's handlers then run in their own scope, and its result is
    /// saved on its own, so a retry after a failure repeats only that message. Public so tests can drive it.
    /// </summary>
    public async Task<int> DispatchBatchAsync(Type contextType, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(contextType);

        var messages = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var claimedAt = timeProvider.GetUtcNow().UtcDateTime;

            var claimed = await db.OutboxMessages
                .FromSqlRaw(ClaimSql(db), claimedAt, MaxAttempts, BatchSize)
                .ToListAsync(cancellationToken);

            foreach (var message in claimed)
            {
                message.NextAttemptAt = claimedAt + ClaimLease;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claimed;
        });

        foreach (var message in messages)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;

            try
            {
                using var handlerScope = scopeFactory.CreateScope();
                await HandleAsync(handlerScope.ServiceProvider, message);
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

            await db.SaveChangesAsync(cancellationToken);
        }

        return messages.Count;
    }

    private static string ClaimSql(ModuleDbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(OutboxMessage))!;
        var table = $"\"{entity.GetSchema() ?? db.Schema}\".\"{entity.GetTableName()}\"";

        return $$"""
            SELECT * FROM {{table}}
            WHERE "ProcessedAt" IS NULL AND "Attempts" < {1} AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {0})
            ORDER BY "OccurredAt"
            LIMIT {2}
            FOR UPDATE SKIP LOCKED
            """;
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
