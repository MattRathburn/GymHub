using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GymHub.BuildingBlocks.ScaleOut;

/// <summary>
/// Messages for every running API instance, for in-memory state that each instance keeps for itself (such as which
/// SignalR connections it holds). Fire and forget: an instance that is offline when a message is published misses it.
/// </summary>
public interface IInstanceMessages
{
    Task PublishAsync<T>(string channel, T message);

    /// <summary>Runs <paramref name="handler"/> on this instance for every message published on the channel.</summary>
    Task SubscribeAsync<T>(string channel, Func<T, Task> handler);
}

public static class ScaleOutExtensions
{
    /// <summary>Aspire connection name of the Redis that lets several API instances work together.</summary>
    public const string RedisConnectionName = "redis";

    /// <summary>
    /// With a "redis" connection string: SignalR uses it as a backplane, so a message reaches clients on every
    /// instance, and <see cref="IInstanceMessages"/> goes through Redis pub/sub. Without one (tests, a single
    /// instance) both stay in-process.
    /// </summary>
    public static IHostApplicationBuilder AddScaleOut(this IHostApplicationBuilder builder)
    {
        var signalR = builder.Services.AddSignalR();
        var redis = builder.Configuration.GetConnectionString(RedisConnectionName);

        if (string.IsNullOrEmpty(redis))
        {
            builder.Services.AddSingleton<IInstanceMessages, LocalInstanceMessages>();
            return builder;
        }

        builder.AddRedisClient(RedisConnectionName);
        signalR.AddStackExchangeRedis(redis, options => options.Configuration.ChannelPrefix = RedisChannel.Literal("gymhub-api"));
        builder.Services.AddSingleton<IInstanceMessages, RedisInstanceMessages>();
        return builder;
    }
}

internal sealed class RedisInstanceMessages(IConnectionMultiplexer redis, ILogger<RedisInstanceMessages> logger) : IInstanceMessages
{
    public async Task PublishAsync<T>(string channel, T message) =>
        await redis.GetSubscriber().PublishAsync(Channel(channel), JsonSerializer.Serialize(message, JsonSerializerOptions.Web));

    public async Task SubscribeAsync<T>(string channel, Func<T, Task> handler)
    {
        var queue = await redis.GetSubscriber().SubscribeAsync(Channel(channel));

        queue.OnMessage(async received =>
        {
            try
            {
                await handler(JsonSerializer.Deserialize<T>(received.Message.ToString(), JsonSerializerOptions.Web)!);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handling instance message on {Channel} failed", channel);
            }
        });
    }

    private static RedisChannel Channel(string channel) => RedisChannel.Literal($"gymhub-instances:{channel}");
}

/// <summary>A single instance: a published message goes straight to this instance's handlers.</summary>
internal sealed class LocalInstanceMessages : IInstanceMessages
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<Func<object, Task>>> _handlers = new();

    public async Task PublishAsync<T>(string channel, T message)
    {
        if (_handlers.TryGetValue(channel, out var handlers))
        {
            foreach (var handler in handlers)
            {
                await handler(message!);
            }
        }
    }

    public Task SubscribeAsync<T>(string channel, Func<T, Task> handler)
    {
        _handlers.GetOrAdd(channel, _ => []).Add(message => handler((T)message));
        return Task.CompletedTask;
    }
}
