using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Reviews.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews.Features.ReportReview;

public sealed record ReportReviewRequest(ReportReason Reason, string? Note);

internal sealed class ReportReviewValidator : AbstractValidator<ReportReviewRequest>
{
    public ReportReviewValidator()
    {
        RuleFor(r => r.Reason).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(ReviewReport.MaxNoteLength);
    }
}

/// <summary>Any signed-in user flags a visible review for admins. Reporting the same review again is a no-op.</summary>
internal sealed class ReportReviewHandler(ReviewsDbContext db, TimeProvider timeProvider)
{
    public async Task<Result> Handle(Guid reviewId, ReportReviewRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null || review.IsHidden)
        {
            return Error.NotFound("Review not found.");
        }

        if (await db.Reports.AnyAsync(r => r.ReviewId == reviewId && r.ReporterId == user.Id && r.Status == ReportStatus.Open, cancellationToken))
        {
            return Result.Success();
        }

        var report = ReviewReport.File(review, user.Id, request.Reason, request.Note, timeProvider.GetUtcNow().UtcDateTime);

        if (!report.IsSuccess)
        {
            return report.Error!;
        }

        db.Reports.Add(report.Value!);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent duplicate submit may have won the unique index; then the report exists either way.
            if (!await db.Reports.AsNoTracking().AnyAsync(
                    r => r.ReviewId == reviewId && r.ReporterId == user.Id && r.Status == ReportStatus.Open, cancellationToken))
            {
                throw;
            }
        }

        return Result.Success();
    }
}

internal static class ReportReviewEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{reviewId:guid}/reports", async (
                    Guid reviewId, ReportReviewRequest request, ClaimsPrincipal user, ReportReviewHandler handler, CancellationToken ct) =>
                (await handler.Handle(reviewId, request, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithValidation<ReportReviewRequest>()
            .WithName("ReportReview");
}
