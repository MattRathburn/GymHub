using System.Collections.Concurrent;
using EventBus.Abstractions;
using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Discussions.Features.Shared;
using GymHub.Modules.Subscriptions.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace GymHub.Modules.Discussions.Realtime;

/// <summary>
/// Which hub groups each connection joined, and for which program, so access can be taken away from open
/// connections. SignalR can't list a group's members. In-memory, like the hub itself (single instance).
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
}

/// <summary>
/// Takes a user who left a program out of its board and thread groups, unless they still have access (re-enrolled,
/// or they're its trainer or an admin). Idempotent.
/// </summary>
internal sealed class RevokeBoardAccessHandler(BoardConnections connections, BoardAccess access, IHubContext<DiscussionsHub> hub)
    : IIntegrationEventHandler<EnrollmentCancelled>
{
    public async Task Handle(EnrollmentCancelled @event)
    {
        var joined = connections.For(@event.UserId, @event.ProgramId);

        if (joined.Count == 0 || (await access.OpenAsync(@event.ProgramId, joined[0].User, CancellationToken.None)).IsSuccess)
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
