using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions.Features.Shared;

internal static class ThreadLookup
{
    /// <summary>Loads a thread (tracked) and checks the caller's access to its board. Hidden threads exist only for moderators.</summary>
    public static async Task<Result<(DiscussionThread Thread, Board Board)>> OpenThreadAsync(
        this BoardAccess access, DiscussionsDbContext db, Guid threadId, CurrentUser user, CancellationToken cancellationToken)
    {
        var thread = await db.Threads.SingleOrDefaultAsync(t => t.Id == threadId, cancellationToken);

        if (thread is null)
        {
            return Error.NotFound("Thread not found.");
        }

        var board = await access.OpenAsync(thread.ProgramId, user, cancellationToken);

        if (!board.IsSuccess)
        {
            return board.Error!;
        }

        if (thread.IsHidden && !board.Value!.CanModerate)
        {
            return Error.NotFound("Thread not found.");
        }

        return (thread, board.Value!);
    }

    /// <summary>Loads a post (tracked) with its thread, checking board access as <see cref="OpenThreadAsync"/> does.</summary>
    public static async Task<Result<(Post Post, DiscussionThread Thread, Board Board)>> OpenPostAsync(
        this BoardAccess access, DiscussionsDbContext db, Guid postId, CurrentUser user, CancellationToken cancellationToken)
    {
        var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == postId, cancellationToken);

        if (post is null)
        {
            return Error.NotFound("Post not found.");
        }

        var opened = await access.OpenThreadAsync(db, post.ThreadId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        return (post, opened.Value.Thread, opened.Value.Board);
    }
}
