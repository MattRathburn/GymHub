using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Reviews.Domain;
using GymHub.Modules.Reviews.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews.Features.ReviewReports;

public sealed record ReviewReportDto(
    Guid Id, ReportReason Reason, string? Note, ReportStatus Status, DateTime CreatedAt, string ReporterId,
    Guid ProgramId, string ProgramTitle, ReviewDto Review);

/// <summary>
/// Admins work through reported reviews: dismiss a report, or act on it (hide the review, which drops it from the
/// rating, and close its open reports).
/// </summary>
internal sealed class ReviewReportsHandler(
    ReviewsDbContext db, ReviewMapper mapper, IProgramCatalog catalog, RatingWriter ratings, TimeProvider timeProvider)
{
    public async Task<PagedResult<ReviewReportDto>> List(ReportStatus status, PaginationRequest page, CancellationToken cancellationToken)
    {
        var reports = db.Reports.AsNoTracking().Where(r => r.Status == status);
        var count = await reports.LongCountAsync(cancellationToken);

        var rows = await reports
            .OrderBy(r => r.CreatedAt)
            .Skip(page.Skip)
            .Take(page.Take)
            .Join(db.Reviews, r => r.ReviewId, v => v.Id, (r, v) => new { Report = r, Review = v })
            .ToListAsync(cancellationToken);

        var reviews = (await mapper.MapAsync([.. rows.Select(x => x.Review).DistinctBy(r => r.Id)], cancellationToken)).ToDictionary(r => r.Id);
        var programs = await catalog.GetSummariesAsync([.. rows.Select(x => x.Report.ProgramId).Distinct()], cancellationToken);

        var dtos = rows.Select(x => new ReviewReportDto(
            x.Report.Id, x.Report.Reason, x.Report.Note, x.Report.Status, x.Report.CreatedAt, x.Report.ReporterId,
            x.Report.ProgramId, programs.GetValueOrDefault(x.Report.ProgramId)?.Title ?? "Unknown program",
            reviews[x.Review.Id])).ToList();

        return new PagedResult<ReviewReportDto>(page.PageIndex, page.Take, count, dtos);
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
        var programId = await db.Reports.Where(r => r.Id == reportId).Select(r => (Guid?)r.ProgramId).SingleOrDefaultAsync(cancellationToken);

        if (programId is null)
        {
            return Error.NotFound("Report not found.");
        }

        return await ratings.RunAsync(programId.Value, async ct =>
        {
            var report = await db.Reports.SingleAsync(r => r.Id == reportId, ct);

            if (report.Status != ReportStatus.Open)
            {
                return Error.Conflict($"This report was already {report.Status.ToString().ToLowerInvariant()}.");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var review = await db.Reviews.SingleAsync(r => r.Id == report.ReviewId, ct);
            review.SetHidden(true, admin.Id, now);

            var openOnReview = await db.Reports.Where(r => r.ReviewId == report.ReviewId && r.Status == ReportStatus.Open).ToListAsync(ct);

            foreach (var open in openOnReview)
            {
                open.Resolve(ReportStatus.Actioned, admin.Id, now);
            }

            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>Undoes a hide (e.g. a report actioned by mistake): the review is visible and counts again.</summary>
    public async Task<Result> Show(Guid reviewId, CancellationToken cancellationToken)
    {
        var programId = await db.Reviews.Where(r => r.Id == reviewId).Select(r => (Guid?)r.ProgramId).SingleOrDefaultAsync(cancellationToken);

        if (programId is null)
        {
            return Error.NotFound("Review not found.");
        }

        return await ratings.RunAsync(programId.Value, async ct =>
        {
            var review = await db.Reviews.SingleAsync(r => r.Id == reviewId, ct);
            review.SetHidden(false, adminId: "", timeProvider.GetUtcNow().UtcDateTime);
            return Result.Success();
        }, cancellationToken);
    }
}

internal static class ReviewReportsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/reports").RequireAuthorization(Policies.Admin);

        reports.MapGet("/", async ([AsParameters] PaginationRequest page, ReviewReportsHandler handler, CancellationToken ct, ReportStatus status = ReportStatus.Open) =>
                await handler.List(status, page, ct))
            .WithName("ListReviewReports");

        reports.MapPost("/{reportId:guid}/dismiss", async (Guid reportId, ClaimsPrincipal user, ReviewReportsHandler handler, CancellationToken ct) =>
                (await handler.Dismiss(reportId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("DismissReviewReport");

        reports.MapPost("/{reportId:guid}/action", async (Guid reportId, ClaimsPrincipal user, ReviewReportsHandler handler, CancellationToken ct) =>
                (await handler.Action(reportId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("ActionReviewReport");

        reports.MapPost("/reviews/{reviewId:guid}/show", async (Guid reviewId, ReviewReportsHandler handler, CancellationToken ct) =>
                (await handler.Show(reviewId, ct)).ToHttp())
            .WithName("ShowReview");
    }
}
