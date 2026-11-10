using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Discussions.Features.ModeratePost;

public sealed record ModeratePostRequest(bool Hidden);

/// <summary>The program's trainer or an admin hides a post (or shows it again). Hiding the answer keeps it marked.</summary>
internal sealed class ModeratePostHandler(DiscussionsDbContext db, BoardAccess access, IBoardNotifier notifier, TimeProvider timeProvider)
{
    public async Task<Result> Handle(Guid postId, ModeratePostRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await access.OpenPostAsync(db, postId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (post, thread, board) = opened.Value;

        if (!board.CanModerate)
        {
            return Error.Forbidden("Only the program's trainer or an admin can moderate this board.");
        }

        post.SetHidden(request.Hidden, user.Id, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);

        await notifier.ThreadChangedAsync(thread.ProgramId, thread.Id);
        return Result.Success();
    }
}

internal static class ModeratePostEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/posts/{postId:guid}/hidden", async (
                    Guid postId, ModeratePostRequest request, ClaimsPrincipal user, ModeratePostHandler handler, CancellationToken ct) =>
                (await handler.Handle(postId, request, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("ModeratePost");
}
