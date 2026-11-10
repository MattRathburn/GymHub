using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Features.Shared;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Discussions.Features.ModerateThread;

/// <summary>Sets any of the thread's flags; omitted ones stay as they are.</summary>
public sealed record ModerateThreadRequest(bool? Pinned, bool? Locked, bool? Hidden);

internal sealed class ModerateThreadValidator : AbstractValidator<ModerateThreadRequest>
{
    public ModerateThreadValidator() =>
        RuleFor(r => r).Must(r => r.Pinned is not null || r.Locked is not null || r.Hidden is not null)
            .WithMessage("Set at least one of pinned, locked or hidden.");
}

/// <summary>The program's trainer or an admin pins, locks or hides a thread.</summary>
internal sealed class ModerateThreadHandler(DiscussionsDbContext db, BoardAccess access, IBoardNotifier notifier, TimeProvider timeProvider)
{
    public async Task<Result> Handle(Guid threadId, ModerateThreadRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await access.OpenThreadAsync(db, threadId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (thread, board) = opened.Value;

        if (!board.CanModerate)
        {
            return Error.Forbidden("Only the program's trainer or an admin can moderate this board.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (request.Pinned is { } pinned)
        {
            thread.SetPinned(pinned, now);
        }

        if (request.Locked is { } locked)
        {
            thread.SetLocked(locked, now);
        }

        if (request.Hidden is { } hidden)
        {
            thread.SetHidden(hidden, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.ThreadChangedAsync(thread.ProgramId, thread.Id);
        return Result.Success();
    }
}

internal static class ModerateThreadEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/threads/{threadId:guid}/moderation", async (
                    Guid threadId, ModerateThreadRequest request, ClaimsPrincipal user, ModerateThreadHandler handler, CancellationToken ct) =>
                (await handler.Handle(threadId, request, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithValidation<ModerateThreadRequest>()
            .WithName("ModerateThread");
}
