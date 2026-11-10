using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Discussions.Realtime;

/// <summary>
/// Live updates for open boards and threads. Clients join a board or thread group (access is checked like the REST
/// endpoints) and receive <see cref="HubBoardNotifier.ThreadChanged"/> messages with ids, then refetch over REST.
/// </summary>
[Authorize]
internal sealed class DiscussionsHub(BoardAccess access, DiscussionsDbContext db, BoardConnections connections) : Hub
{
    public async Task JoinBoard(Guid programId)
    {
        var user = RequireUser();
        var board = await access.OpenAsync(programId, user, Context.ConnectionAborted);

        if (!board.IsSuccess)
        {
            throw new HubException(board.Error!.Message);
        }

        await JoinAsync(user, BoardGroups.Board(programId), programId);
    }

    public Task LeaveBoard(Guid programId) => LeaveAsync(BoardGroups.Board(programId));

    public async Task JoinThread(Guid threadId)
    {
        // Same check as the REST endpoints, so hidden threads are only joinable by moderators.
        var user = RequireUser();
        var opened = await access.OpenThreadAsync(db, threadId, user, Context.ConnectionAborted);

        if (!opened.IsSuccess)
        {
            throw new HubException(opened.Error!.Message);
        }

        await JoinAsync(user, BoardGroups.Thread(threadId), opened.Value.Thread.ProgramId);
    }

    public Task LeaveThread(Guid threadId) => LeaveAsync(BoardGroups.Thread(threadId));

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private CurrentUser RequireUser() => CurrentUser.From(Context.User!) ?? throw new HubException("Sign in to join a board.");

    private async Task JoinAsync(CurrentUser user, string group, Guid programId)
    {
        connections.Add(Context.ConnectionId, user, group, programId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    private async Task LeaveAsync(string group)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        connections.Remove(Context.ConnectionId, group);
    }
}

internal sealed class HubBoardNotifier(IHubContext<DiscussionsHub> hub, ILogger<HubBoardNotifier> logger) : IBoardNotifier
{
    /// <summary>Client method name. Arguments: programId, threadId.</summary>
    public const string ThreadChanged = "threadChanged";

    public async Task ThreadChangedAsync(Guid programId, Guid threadId)
    {
        try
        {
            await hub.Clients
                .Groups(BoardGroups.Board(programId), BoardGroups.Thread(threadId))
                .SendAsync(ThreadChanged, programId, threadId);
        }
        catch (Exception ex)
        {
            // The change is saved; live updates are best effort.
            logger.LogWarning(ex, "Couldn't push a live update for thread {ThreadId}", threadId);
        }
    }
}
