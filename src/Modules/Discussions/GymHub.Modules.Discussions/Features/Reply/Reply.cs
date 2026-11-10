using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Domain;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions.Features.Reply;

/// <summary>ParentPostId is the post being answered: the opening post or another reply.</summary>
public sealed record ReplyRequest(Guid ParentPostId, string Body);

public sealed record ReplyResponse(Guid Id, Guid ThreadId, int Depth);

internal sealed class ReplyValidator : AbstractValidator<ReplyRequest>
{
    public ReplyValidator()
    {
        RuleFor(r => r.ParentPostId).NotEmpty();
        RuleFor(r => r.Body).NotEmpty().MaximumLength(Post.MaxBodyLength);
    }
}

internal sealed class ReplyHandler(DiscussionsDbContext db, BoardAccess access, IBoardNotifier notifier, TimeProvider timeProvider)
{
    public async Task<Result<ReplyResponse>> Handle(
        Guid threadId, ReplyRequest request, CurrentUser user, string authorName, CancellationToken cancellationToken)
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

        var parent = await db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.ParentPostId, cancellationToken);

        if (parent is null)
        {
            return Error.NotFound("The post you're replying to doesn't exist.");
        }

        var reply = thread.Reply(parent, user.Id, authorName, request.Body, board.CanModerate, timeProvider.GetUtcNow().UtcDateTime);

        if (!reply.IsSuccess)
        {
            return reply.Error!;
        }

        db.Posts.Add(reply.Value!);
        await db.SaveChangesAsync(cancellationToken);

        await notifier.ThreadChangedAsync(thread.ProgramId, thread.Id);
        return new ReplyResponse(reply.Value!.Id, thread.Id, reply.Value.Depth);
    }
}

internal static class ReplyEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/threads/{threadId:guid}/posts", async (
                    Guid threadId, ReplyRequest request, ClaimsPrincipal user, ReplyHandler handler, CancellationToken ct) =>
                (await handler.Handle(threadId, request, CurrentUser.Required(user), AuthorLookup.SnapshotName(user), ct))
                    .ToHttp(r => TypedResults.Created($"/api/v1/discussions/threads/{threadId}", r)))
            .RequireAuthorization()
            .WithValidation<ReplyRequest>()
            .WithName("Reply");
}
