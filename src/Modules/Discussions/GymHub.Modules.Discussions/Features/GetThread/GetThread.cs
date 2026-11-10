using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Domain;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions.Features.GetThread;

/// <summary>
/// One post. Body is null when the post was deleted, or hidden and the viewer isn't a moderator
/// (moderators still see hidden text, to review it). Author is null for deleted posts.
/// </summary>
public sealed record PostDto(
    Guid Id, Guid? ParentPostId, int Depth, AuthorDto? Author, string? Body, DateTime CreatedAt, DateTime? EditedAt,
    bool IsDeleted, bool IsHidden, bool IsMine, bool IsAnswer);

/// <summary>A thread and all its posts as a flat list in posting order; clients build the tree from ParentPostId.</summary>
public sealed record ThreadDto(
    Guid Id, Guid ProgramId, string ProgramTitle, ThreadKind Kind, string Title, AuthorDto Author, DateTime CreatedAt,
    bool IsPinned, bool IsLocked, bool IsHidden, Guid? AnswerPostId, bool CanReply, bool CanModerate, bool CanMarkAnswer,
    bool HasMorePosts, IReadOnlyList<PostDto> Posts);

internal sealed class GetThreadHandler(DiscussionsDbContext db, BoardAccess access, AuthorLookup authors)
{
    /// <summary>Threads load whole; this caps the very largest until replies are paged.</summary>
    public const int MaxPosts = 500;

    public async Task<Result<ThreadDto>> Handle(Guid threadId, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await access.OpenThreadAsync(db, threadId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (thread, board) = opened.Value;

        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.ThreadId == threadId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Take(MaxPosts + 1)
            .ToListAsync(cancellationToken);

        var hasMore = posts.Count > MaxPosts;

        if (hasMore)
        {
            posts.RemoveAt(posts.Count - 1);
        }

        var author = await authors.LoadAsync(posts.Select(p => p.AuthorId).Append(thread.AuthorId), board.TrainerId, cancellationToken);
        var opening = posts.FirstOrDefault(p => p.IsOpeningPost);

        var dtos = posts.Select(p => new PostDto(
            p.Id, p.ParentPostId, p.Depth,
            p.IsDeleted ? null : author(p.AuthorId, p.AuthorName),
            p.IsDeleted || (p.IsHidden && !board.CanModerate) ? null : p.Body,
            p.CreatedAt, p.EditedAt, p.IsDeleted, p.IsHidden,
            p.AuthorId == user.Id && !p.IsDeleted,
            p.Id == thread.AnswerPostId)).ToList();

        var canReply = board.CanPost && !thread.IsHidden && (!thread.IsLocked || board.CanModerate);
        var canMarkAnswer = thread.Kind == ThreadKind.Question && (thread.AuthorId == user.Id || board.CanModerate);

        return new ThreadDto(
            thread.Id, thread.ProgramId, board.ProgramTitle, thread.Kind, thread.Title,
            author(thread.AuthorId, opening?.AuthorName ?? "Member"), thread.CreatedAt,
            thread.IsPinned, thread.IsLocked, thread.IsHidden, thread.AnswerPostId,
            canReply, board.CanModerate, canMarkAnswer, hasMore, dtos);
    }
}

internal static class GetThreadEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/threads/{threadId:guid}", async (Guid threadId, ClaimsPrincipal user, GetThreadHandler handler, CancellationToken ct) =>
                (await handler.Handle(threadId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("GetThread");
}
