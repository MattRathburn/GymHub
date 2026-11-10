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

namespace GymHub.Modules.Discussions.Features.StartThread;

public sealed record StartThreadRequest(ThreadKind Kind, string Title, string Body);

public sealed record StartThreadResponse(Guid Id, Guid OpeningPostId);

internal sealed class StartThreadValidator : AbstractValidator<StartThreadRequest>
{
    public StartThreadValidator()
    {
        RuleFor(r => r.Kind).IsInEnum();
        RuleFor(r => r.Title).NotEmpty().MaximumLength(DiscussionThread.MaxTitleLength);
        RuleFor(r => r.Body).NotEmpty().MaximumLength(Post.MaxBodyLength);
    }
}

internal sealed class StartThreadHandler(DiscussionsDbContext db, BoardAccess access, IBoardNotifier notifier, TimeProvider timeProvider)
{
    public async Task<Result<StartThreadResponse>> Handle(
        Guid programId, StartThreadRequest request, CurrentUser user, string authorName, CancellationToken cancellationToken)
    {
        var board = await access.OpenAsync(programId, user, cancellationToken);

        if (!board.IsSuccess)
        {
            return board.Error!;
        }

        if (!board.Value!.CanPost)
        {
            return Error.Conflict("This program was archived, so its board is read-only.");
        }

        var started = DiscussionThread.Start(
            programId, request.Kind, request.Title, request.Body, user.Id, authorName, timeProvider.GetUtcNow().UtcDateTime);

        if (!started.IsSuccess)
        {
            return started.Error!;
        }

        var (thread, post) = started.Value;
        db.Threads.Add(thread);
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        await notifier.ThreadChangedAsync(programId, thread.Id);
        return new StartThreadResponse(thread.Id, post.Id);
    }
}

internal static class StartThreadEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/programs/{programId:guid}/threads", async (
                    Guid programId, StartThreadRequest request, ClaimsPrincipal user, StartThreadHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, request, CurrentUser.Required(user), AuthorLookup.SnapshotName(user), ct))
                    .ToHttp(r => TypedResults.Created($"/api/v1/discussions/threads/{r.Id}", r)))
            .RequireAuthorization()
            .WithValidation<StartThreadRequest>()
            .WithName("StartThread");
}
