using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions.Features.MarkAnswer;

/// <summary>PostId null clears the answer.</summary>
public sealed record MarkAnswerRequest(Guid? PostId);

/// <summary>The person who asked, the program's trainer or an admin picks the reply that answers a question thread.</summary>
internal sealed class MarkAnswerHandler(DiscussionsDbContext db, BoardAccess access, IBoardNotifier notifier)
{
    public async Task<Result> Handle(Guid threadId, MarkAnswerRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await access.OpenThreadAsync(db, threadId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (thread, board) = opened.Value;

        if (!board.CanPost)
        {
            return Error.Conflict("This program was archived, so its board is read-only.");
        }

        Domain.Post? post = null;

        if (request.PostId is { } postId)
        {
            post = await db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == postId, cancellationToken);

            if (post is null)
            {
                return Error.NotFound("Post not found.");
            }
        }

        var marked = thread.MarkAnswer(post, user.Id, board.CanModerate);

        if (!marked.IsSuccess)
        {
            return marked;
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.ThreadChangedAsync(thread.ProgramId, thread.Id);
        return Result.Success();
    }
}

internal static class MarkAnswerEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/threads/{threadId:guid}/answer", async (
                    Guid threadId, MarkAnswerRequest request, ClaimsPrincipal user, MarkAnswerHandler handler, CancellationToken ct) =>
                (await handler.Handle(threadId, request, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("MarkAnswer");
}
