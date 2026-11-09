using System.Text.Json;
using EventBus.Events;
using GymHub.BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;

namespace GymHub.BuildingBlocks.Persistence;

/// <summary>
/// Base for every module's DbContext: one Postgres schema per module, with that module's outbox in it.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    public abstract string Schema { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Queues an integration event in this module's outbox. It is written by the same SaveChangesAsync
    /// as the state change, and dispatched in-process afterwards by <see cref="OutboxDispatcher"/>.
    /// </summary>
    public void AddIntegrationEvent(IntegrationEvent @event)
    {
        var type = @event.GetType();

        OutboxMessages.Add(new OutboxMessage
        {
            Id = @event.Id,
            Type = type.Name,
            Content = JsonSerializer.Serialize(@event, type, OutboxMessage.SerializerOptions),
            OccurredAt = @event.CreationDate
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
