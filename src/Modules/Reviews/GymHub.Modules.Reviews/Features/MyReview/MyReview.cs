using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Reviews.Contracts;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Reviews.Domain;
using GymHub.Modules.Reviews.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews.Features.MyReview;

public sealed record WriteReviewRequest(int Stars, string? Body);

internal sealed class WriteReviewValidator : AbstractValidator<WriteReviewRequest>
{
    public WriteReviewValidator()
    {
        RuleFor(r => r.Stars).InclusiveBetween(Review.MinStars, Review.MaxStars);
        RuleFor(r => r.Body).MaximumLength(Review.MaxBodyLength);
    }
}

/// <summary>CanWrite says whether the caller may write or edit now; Reason explains why not.</summary>
public sealed record MyReviewDto(ReviewDto? Review, bool CanWrite, string? Reason);

/// <summary>
/// The caller's own review of a program. Writing needs current access; once access ends the review stays (and keeps
/// counting) but can only be deleted.
/// </summary>
internal sealed class MyReviewHandler(ReviewsDbContext db, ProgramGate gate, RatingWriter ratings, ReviewMapper mapper, TimeProvider timeProvider)
{
    public async Task<Result<MyReviewDto>> Get(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var found = await gate.FindAsync(programId, cancellationToken);

        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var review = await db.Reviews.AsNoTracking().SingleOrDefaultAsync(r => r.ProgramId == programId && r.AuthorId == user.Id, cancellationToken);
        var allowed = await gate.CanWriteAsync(programId, user, cancellationToken);

        return new MyReviewDto(
            review is null ? null : await mapper.MapAsync(review, cancellationToken),
            allowed.IsSuccess,
            allowed.Error?.Message);
    }

    public async Task<Result<ReviewDto>> Write(
        Guid programId, WriteReviewRequest request, CurrentUser user, string authorName, CancellationToken cancellationToken)
    {
        var allowed = await gate.CanWriteAsync(programId, user, cancellationToken);

        if (!allowed.IsSuccess)
        {
            return allowed.Error!;
        }

        var program = allowed.Value!;
        Review? saved = null;

        var result = await ratings.RunAsync(programId, async ct =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var existing = await db.Reviews.SingleOrDefaultAsync(r => r.ProgramId == programId && r.AuthorId == user.Id, ct);

            if (existing is not null)
            {
                saved = existing;
                return existing.Edit(request.Stars, request.Body, now);
            }

            var written = Review.Write(programId, program.TrainerId, user.Id, authorName, request.Stars, request.Body, now);

            if (!written.IsSuccess)
            {
                return written.Error!;
            }

            saved = written.Value!;
            db.Reviews.Add(saved);
            db.AddIntegrationEvent(new ReviewPosted(saved.Id, programId, saved.TrainerId, user.Id, saved.Stars));
            return Result.Success();
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        return await mapper.MapAsync(saved!, cancellationToken);
    }

    public Task<Result> Delete(Guid programId, CurrentUser user, CancellationToken cancellationToken) =>
        ratings.RunAsync(programId, async ct =>
        {
            var review = await db.Reviews.SingleOrDefaultAsync(r => r.ProgramId == programId && r.AuthorId == user.Id, ct);

            if (review is null)
            {
                return Error.NotFound("You haven't reviewed this program.");
            }

            // Deleting would let the author post it again visibly, and would drop its reports before an admin sees them.
            if (review.IsHidden)
            {
                return Error.Conflict("This review was hidden by a moderator, so it can't be deleted.");
            }

            if (await db.Reports.AnyAsync(r => r.ReviewId == review.Id && r.Status == ReportStatus.Open, ct))
            {
                return Error.Conflict("This review is being reviewed by a moderator and can't be deleted yet.");
            }

            db.Reviews.Remove(review);
            return Result.Success();
        }, cancellationToken);
}

internal static class MyReviewEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/programs/{programId:guid}/mine").RequireAuthorization();

        mine.MapGet("/", async (Guid programId, ClaimsPrincipal user, MyReviewHandler handler, CancellationToken ct) =>
                (await handler.Get(programId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("GetMyReview");

        mine.MapPut("/", async (Guid programId, WriteReviewRequest request, ClaimsPrincipal user, MyReviewHandler handler, CancellationToken ct) =>
                (await handler.Write(programId, request, CurrentUser.Required(user), ReviewMapper.SnapshotName(user), ct)).ToHttp())
            .WithValidation<WriteReviewRequest>()
            .WithName("WriteMyReview");

        mine.MapDelete("/", async (Guid programId, ClaimsPrincipal user, MyReviewHandler handler, CancellationToken ct) =>
                (await handler.Delete(programId, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("DeleteMyReview");
    }
}
