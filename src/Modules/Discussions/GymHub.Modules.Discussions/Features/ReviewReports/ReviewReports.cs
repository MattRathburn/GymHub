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

namespace GymHub.Modules.Discussions.Features.ReviewReports;

public sealed record ReportDto(
    Guid Id, ReportReason Reason, string? Note, ReportStatus Status, DateTime CreatedAt, string ReporterId,
    Guid ProgramId, Guid ThreadId, string ThreadTitle, Guid PostId, string PostAuthorId, string PostAuthorName, string PostBody,
    bool PostIsHidden);

/// <summary>Admins work through reported posts: dismiss a report, or act on it (hide the post and close its open reports).</summary>
internal sealed class ReviewReportsHandler(DiscussionsDbContext db, AuthorLookup authors, IBoardNotifier notifier, TimeProvider timeProvider)
{
    public async Task<PagedResult<ReportDto>> List(ReportStatus status, PaginationRequest page, CancellationToken cancellationToken)
    {
        var reports = db.Reports.AsNoTracking().Where(r => r.Status == status);
        var count = await reports.LongCountAsync(cancellationToken);

        var rows = await reports
            .OrderBy(r => r.CreatedAt)
            .Skip(page.Skip)
            .Take(page.Take)
            .Join(db.Posts, r => r.PostId, p => p.Id, (r, p) => new { Report = r, Post = p })
            .Join(db.Threads, x => x.Post.ThreadId, t => t.Id, (x, t) => new { x.Report, x.Post, ThreadTitle = t.Title })
            .ToListAsync(cancellationToken);

        // Current profile names; no program's trainer to badge here.
        var author = await authors.LoadAsync(rows.Select(x => x.Post.AuthorId), trainerId: "", cancellationToken);

        var dtos = rows.Select(x => new ReportDto(
            x.Report.Id, x.Report.Reason, x.Report.Note, x.Report.Status, x.Report.CreatedAt, x.Report.ReporterId,
            x.Report.ProgramId, x.Report.ThreadId, x.ThreadTitle, x.Post.Id, x.Post.AuthorId,
            author(x.Post.AuthorId, x.Post.AuthorName).DisplayName, x.Post.Body, x.Post.IsHidden)).ToList();

        return new PagedResult<ReportDto>(page.PageIndex, page.Take, count, dtos);
    }

    public async Task<Result> Dismiss(Guid reportId, CurrentUser admin, CancellationToken cancellationToken)
    {
        var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == reportId, cancellationToken);

        if (report is null)
        {
            return Error.NotFound("Report not found.");
        }

        var resolved = report.Resolve(ReportStatus.Dismissed, admin.Id, timeProvider.GetUtcNow().UtcDateTime);

        if (!resolved.IsSuccess)
        {
            return resolved;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Action(Guid reportId, CurrentUser admin, CancellationToken cancellationToken)
    {
        var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == reportId, cancellationToken);

        if (report is null)
        {
            return Error.NotFound("Report not found.");
        }

        if (report.Status != ReportStatus.Open)
        {
            return Error.Conflict($"This report was already {report.Status.ToString().ToLowerInvariant()}.");
        }

        var post = await db.Posts.SingleAsync(p => p.Id == report.PostId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        post.SetHidden(true, admin.Id, now);

        var openOnPost = await db.Reports.Where(r => r.PostId == report.PostId && r.Status == ReportStatus.Open).ToListAsync(cancellationToken);

        foreach (var open in openOnPost)
        {
            open.Resolve(ReportStatus.Actioned, admin.Id, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.ThreadChangedAsync(report.ProgramId, report.ThreadId);
        return Result.Success();
    }
}

internal static class ReviewReportsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/reports").RequireAuthorization(Policies.Admin);

        reports.MapGet("/", async ([AsParameters] PaginationRequest page, ReviewReportsHandler handler, CancellationToken ct, ReportStatus status = ReportStatus.Open) =>
                await handler.List(status, page, ct))
            .WithName("ListReports");

        reports.MapPost("/{reportId:guid}/dismiss", async (Guid reportId, ClaimsPrincipal user, ReviewReportsHandler handler, CancellationToken ct) =>
                (await handler.Dismiss(reportId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("DismissReport");

        reports.MapPost("/{reportId:guid}/action", async (Guid reportId, ClaimsPrincipal user, ReviewReportsHandler handler, CancellationToken ct) =>
                (await handler.Action(reportId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("ActionReport");
    }
}
