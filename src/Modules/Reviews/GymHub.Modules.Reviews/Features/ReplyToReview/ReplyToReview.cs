using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Reviews.Domain;
using GymHub.Modules.Reviews.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews.Features.ReplyToReview;

public sealed record ReplyRequest(string Body);

internal sealed class ReplyValidator : AbstractValidator<ReplyRequest>
{
    public ReplyValidator()
    {
        RuleFor(r => r.Body).NotEmpty().MaximumLength(Review.MaxReplyLength);
    }
}

/// <summary>The program's trainer answers a review publicly: one reply per review, which they can edit or remove.</summary>
internal sealed class ReplyToReviewHandler(ReviewsDbContext db, ReviewMapper mapper, TimeProvider timeProvider)
{
    public async Task<Result<ReviewDto>> Reply(Guid reviewId, ReplyRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(reviewId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var review = opened.Value!;
        var replied = review.SetReply(request.Body, timeProvider.GetUtcNow().UtcDateTime);

        if (!replied.IsSuccess)
        {
            return replied.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.MapAsync(review, cancellationToken);
    }

    public async Task<Result> Remove(Guid reviewId, CurrentUser user, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(reviewId, user, cancellationToken);

        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        opened.Value!.RemoveReply();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result<Review>> OpenAsync(Guid reviewId, CurrentUser user, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null || review.IsHidden)
        {
            return Error.NotFound("Review not found.");
        }

        if (review.TrainerId != user.Id)
        {
            return Error.Forbidden("Only the program's trainer can reply to its reviews.");
        }

        return review;
    }
}

internal static class ReplyToReviewEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/{reviewId:guid}/reply", async (Guid reviewId, ReplyRequest request, ClaimsPrincipal user, ReplyToReviewHandler handler, CancellationToken ct) =>
                (await handler.Reply(reviewId, request, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithValidation<ReplyRequest>()
            .WithName("ReplyToReview");

        app.MapDelete("/{reviewId:guid}/reply", async (Guid reviewId, ClaimsPrincipal user, ReplyToReviewHandler handler, CancellationToken ct) =>
                (await handler.Remove(reviewId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("RemoveReviewReply");
    }
}
