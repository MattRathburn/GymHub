using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Domain;
using GymHub.Modules.Discussions.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions.Features.ListThreads;

public sealed record ThreadSummaryDto(
    Guid Id, ThreadKind Kind, string Title, AuthorDto Author, DateTime CreatedAt, DateTime LastActivityAt, int ReplyCount,
    bool IsPinned, bool IsLocked, bool IsHidden, bool IsAnswered);

public sealed record BoardDto(Guid ProgramId, string ProgramTitle, string TrainerId, bool CanPost, bool CanModerate, PagedResult<ThreadSummaryDto> Threads);

public sealed record ListThreadsQuery(ThreadKind? Kind, string? Q);

/// <summary>A program's board: pinned threads first, then the most recently active. Hidden threads only for moderators.</summary>
internal sealed class ListThreadsHandler(DiscussionsDbContext db, BoardAccess access, AuthorLookup authors)
{
    public async Task<Result<BoardDto>> Handle(Guid programId, ListThreadsQuery query, PaginationRequest page, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await access.OpenAsync(programId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var board = opened.Value!;
        var threads = db.Threads.AsNoTracking().Where(t => t.ProgramId == programId);

        if (!board.CanModerate)
        {
            threads = threads.Where(t => t.HiddenAt == null);
        }

        if (query.Kind is { } kind)
        {
            threads = threads.Where(t => t.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{query.Q.Trim()}%";
            threads = threads.Where(t => EF.Functions.ILike(t.Title, pattern));
        }

        var count = await threads.LongCountAsync(cancellationToken);

        var rows = await threads
            .OrderByDescending(t => t.PinnedAt != null)
            .ThenByDescending(t => t.LastActivityAt)
            .Skip(page.Skip)
            .Take(page.Take)
            .Select(t => new
            {
                Thread = t,
                OpeningAuthorName = db.Posts.Where(p => p.ThreadId == t.Id && p.ParentPostId == null).Select(p => p.AuthorName).FirstOrDefault(),
                Replies = db.Posts.Count(p => p.ThreadId == t.Id && p.ParentPostId != null)
            })
            .ToListAsync(cancellationToken);

        var author = await authors.LoadAsync(rows.Select(r => r.Thread.AuthorId), board.TrainerId, cancellationToken);

        var summaries = rows.Select(r => new ThreadSummaryDto(
            r.Thread.Id, r.Thread.Kind, r.Thread.Title, author(r.Thread.AuthorId, r.OpeningAuthorName ?? "Member"),
            r.Thread.CreatedAt, r.Thread.LastActivityAt, r.Replies,
            r.Thread.IsPinned, r.Thread.IsLocked, r.Thread.IsHidden, r.Thread.AnswerPostId is not null)).ToList();

        return new BoardDto(board.ProgramId, board.ProgramTitle, board.TrainerId, board.CanPost, board.CanModerate,
            new PagedResult<ThreadSummaryDto>(page.PageIndex, page.Take, count, summaries));
    }
}

internal static class ListThreadsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/programs/{programId:guid}/threads", async (
                    Guid programId, [AsParameters] ListThreadsQuery query, [AsParameters] PaginationRequest page,
                    ClaimsPrincipal user, ListThreadsHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, query, page, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("ListThreads");
}
