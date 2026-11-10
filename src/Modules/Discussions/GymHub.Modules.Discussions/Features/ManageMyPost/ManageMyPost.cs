using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Domain;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions.Features.ManageMyPost;

public sealed record EditPostRequest(string Body);

internal sealed class EditPostValidator : AbstractValidator<EditPostRequest>
{
    public EditPostValidator() => RuleFor(r => r.Body).NotEmpty().MaximumLength(Post.MaxBodyLength);
}

/// <summary>
/// Authors edit or delete their own posts. Deleting a post with replies leaves a placeholder; deleting a thread's
/// opening post with no replies removes the whole thread.
/// </summary>
internal sealed class ManageMyPostHandler(DiscussionsDbContext db, BoardAccess access, IBoardNotifier notifier, TimeProvider timeProvider)
{
    public async Task<Result> Edit(Guid postId, EditPostRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await access.OpenPostAsync(db, postId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (post, thread, board) = opened.Value;

        if (!board.CanPost)
        {
            return Error.Conflict("This program was archived, so its board is read-only.");
        }

        var edited = post.Edit(user.Id, request.Body, thread.IsLocked || thread.IsHidden, timeProvider.GetUtcNow().UtcDateTime);

        if (!edited.IsSuccess)
        {
            return edited;
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.ThreadChangedAsync(thread.ProgramId, thread.Id);
        return Result.Success();
    }

    public async Task<Result> Delete(Guid postId, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await access.OpenPostAsync(db, postId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (post, thread, board) = opened.Value;

        if (!board.CanPost)
        {
            return Error.Conflict("This program was archived, so its board is read-only.");
        }

        // Deleting would cascade away the reports before an admin sees them.
        if (await db.Reports.AnyAsync(r => r.PostId == postId && r.Status == ReportStatus.Open, cancellationToken))
        {
            return Error.Conflict("This post is being reviewed by a moderator and can't be deleted yet.");
        }

        var hasReplies = await db.Posts.AnyAsync(p => p.ParentPostId == postId, cancellationToken);
        var deleted = post.Delete(user.Id, hasReplies, timeProvider.GetUtcNow().UtcDateTime);

        if (!deleted.IsSuccess)
        {
            return deleted.Error!;
        }

        if (deleted.Value)
        {
            if (post.IsOpeningPost)
            {
                db.Threads.Remove(thread);
            }
            else
            {
                thread.PostRemoved(post.Id);
            }

            db.Posts.Remove(post);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.ThreadChangedAsync(thread.ProgramId, thread.Id);
        return Result.Success();
    }
}

internal static class ManageMyPostEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/posts/{postId:guid}", async (Guid postId, EditPostRequest request, ClaimsPrincipal user, ManageMyPostHandler handler, CancellationToken ct) =>
                (await handler.Edit(postId, request, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithValidation<EditPostRequest>()
            .WithName("EditPost");

        app.MapDelete("/posts/{postId:guid}", async (Guid postId, ClaimsPrincipal user, ManageMyPostHandler handler, CancellationToken ct) =>
                (await handler.Delete(postId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("DeletePost");
    }
}
