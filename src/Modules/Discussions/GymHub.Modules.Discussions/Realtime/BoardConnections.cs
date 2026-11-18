using System.Collections.Concurrent;
using EventBus.Abstractions;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.ScaleOut;
using GymHub.Modules.Discussions.Features.Shared;
using GymHub.Modules.Subscriptions.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Discussions.Realtime;

/// <summary>
/// Which hub groups each connection joined, and for which program, so access can be taken away from open
/// connections. SignalR can't list a group's members. Per instance: each instance tracks the connections it holds.
/// </summary>
internal sealed class BoardConnections
{
    private sealed record Joined(CurrentUser User, ConcurrentDictionary<string, Guid> Groups);

    private readonly ConcurrentDictionary<string, Joined> _connections = new();

    public void Add(string connectionId, CurrentUser user, string group, Guid programId) =>
        _connections.GetOrAdd(connectionId, _ => new Joined(user, new ConcurrentDictionary<string, Guid>())).Groups[group] = programId;

    public void Remove(string connectionId, string group)
    {
        if (_connections.TryGetValue(connectionId, out var joined))
        {
            joined.Groups.TryRemove(group, out _);
        }
    }

    public void Disconnected(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>The user's open connections and the groups they joined for one program.</summary>
    public IReadOnlyList<(string ConnectionId, CurrentUser User, string Group)> For(string userId, Guid programId) =>
        [.. from connection in _connections
            where connection.Value.User.Id == userId
            from g in connection.Value.Groups
            where g.Value == programId
            select (connection.Key, connection.Value.User, g.Key)];

    /// <summary>Every (user, program) this instance has open board or thread groups for.</summary>
    public IReadOnlyList<BoardAccessRevoked> Memberships() =>
        [.. (from connection in _connections
            from g in connection.Value.Groups
            select new BoardAccessRevoked(connection.Value.User.Id, g.Value)).Distinct()];
}

/// <summary>A user lost (or may have lost) access to a program's board.</summary>
internal sealed record BoardAccessRevoked(string UserId, Guid ProgramId);

/// <summary>
/// Hands a cancelled enrollment to every API instance, since each knows only its own connections. Idempotent.
/// </summary>
internal sealed class RevokeBoardAccessHandler(IInstanceMessages instances) : IIntegrationEventHandler<EnrollmentCancelled>
{
    public const string Channel = "discussions:board-access-revoked";

    public Task Handle(EnrollmentCancelled @event) =>
        instances.PublishAsync(Channel, new BoardAccessRevoked(@event.UserId, @event.ProgramId));
}

/// <summary>
/// On each instance: takes a user who left a program out of its board and thread groups on this instance's
/// connections, unless they still have access (re-enrolled, or they're its trainer or an admin).
/// Messages are fire and forget, so one published while this instance's subscription was down is lost. Every
/// <see cref="RecheckInterval"/> the instance therefore re-checks each membership it holds, which catches those.
/// Subscribing retries in the background: the API starts (and serves everything else) while Redis is unreachable.
/// </summary>
internal sealed class BoardAccessRevocations(
    IInstanceMessages instances, BoardConnections connections, IServiceScopeFactory scopes, IHubContext<DiscussionsHub> hub,
    TimeProvider timeProvider, ILogger<BoardAccessRevocations> logger) : BackgroundService
{
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SubscribeAsync(stoppingToken);

        using var timer = new PeriodicTimer(RecheckInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RecheckAllAsync();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Re-checking board access failed");
            }
        }
    }

    private async Task SubscribeAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);

        while (true)
        {
            try
            {
                await instances.SubscribeAsync<BoardAccessRevoked>(RevokeBoardAccessHandler.Channel, RevokeAsync);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Subscribing to board access revocations failed; retrying in {Delay}", delay);
                await Task.Delay(delay, timeProvider, stoppingToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(1).Ticks));
            }
        }
    }

    /// <summary>Re-checks every membership this instance holds. Public to tests.</summary>
    internal async Task RecheckAllAsync()
    {
        foreach (var membership in connections.Memberships())
        {
            await RevokeAsync(membership);
        }
    }

    internal async Task RevokeAsync(BoardAccessRevoked revoked)
    {
        var joined = connections.For(revoked.UserId, revoked.ProgramId);

        if (joined.Count == 0)
        {
            return;
        }

        using var scope = scopes.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<BoardAccess>();

        if ((await access.OpenAsync(revoked.ProgramId, joined[0].User, CancellationToken.None)).IsSuccess)
        {
            return;
        }

        foreach (var (connectionId, _, group) in joined)
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, group);
            connections.Remove(connectionId, group);
        }
    }
}
