using System.Diagnostics.CodeAnalysis;
using EventBus.Abstractions;
using EventBus.Events;
using EventBus.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace GymHub.BuildingBlocks.Outbox;

public static class OutboxExtensions
{
    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services)
    {
        services.AddOptions<EventBusSubscriptionInfo>();
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }

    /// <summary>
    /// Subscribes an in-process handler to an integration event, using EventBus's keyed registration.
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        new InProcessEventBusBuilder(services).AddSubscription<TEvent, THandler>();
        return services;
    }

    private sealed record InProcessEventBusBuilder(IServiceCollection Services) : IEventBusBuilder;
}
