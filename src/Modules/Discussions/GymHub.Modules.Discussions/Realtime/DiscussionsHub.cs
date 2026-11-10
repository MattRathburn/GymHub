using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Discussions.Realtime;

/// <summary>
/// Live updates for open boards and threads. Clients join a board or thread group (access is checked like the REST
/// endpoints) and receive <see cref="HubBoardNotifier.ThreadChanged"/> messages with ids, then refetch over REST.
/// </summary>
[Authorize]
internal sealed class DiscussionsHub(BoardAccess access, DiscussionsDbContext db) : Hub
{
    public async Task JoinBoard(Guid programId)
    {
        await EnsureAccessAsync(programId);
        await Groups.AddToGroupAsync(Context.ConnectionId, BoardGroups.Board(programId), Context.ConnectionAborted);
    }

    public Task LeaveBoard(Guid programId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardGroups.Board(programId), Context.ConnectionAborted);

    public async Task JoinThread(Guid threadId)
    {
        var programId = await db.Threads.AsNoTracking()
            .Where(t => t.Id == threadId)
            .Select(t => (Guid?)t.ProgramId)
            .SingleOrDefaultAsync(Context.ConnectionAborted);

        if (programId is null)
        {
            throw new HubException("Thread not found.");
        }

        await EnsureAccessAsync(programId.Value);
        await Groups.AddToGroupAsync(Context.ConnectionId, BoardGroups.Thread(threadId), Context.ConnectionAborted);
    }

    public Task LeaveThread(Guid threadId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardGroups.Thread(threadId), Context.ConnectionAborted);

    private async Task EnsureAccessAsync(Guid programId)
    {
        var user = CurrentUser.From(Context.User!) ?? throw new HubException("Sign in to join a board.");
        var board = await access.OpenAsync(programId, user, Context.ConnectionAborted);

        if (!board.IsSuccess)
        {
            throw new HubException(board.Error!.Message);
        }
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
