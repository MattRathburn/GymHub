using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Reviews.Contracts;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Reviews.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews.Features.ProgramReviews;

/// <summary>StarCounts[0] is the number of 1-star reviews, up to StarCounts[4] for 5 stars.</summary>
public sealed record RatingSummaryDto(decimal? Average, int Count, IReadOnlyList<int> StarCounts);

public sealed record ProgramReviewsDto(RatingSummaryDto Summary, PagedResult<ReviewDto> Reviews);

/// <summary>A program's visible reviews, newest first, with its rating summary. Public, like the program page.</summary>
internal sealed class ProgramReviewsHandler(ReviewsDbContext db, ProgramGate gate, ReviewMapper mapper)
{
    public async Task<Result<ProgramReviewsDto>> Handle(Guid programId, PaginationRequest page, CancellationToken cancellationToken)
    {
        var found = await gate.FindAsync(programId, cancellationToken);

        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var visible = db.Reviews.AsNoTracking().Where(r => r.ProgramId == programId && !r.IsHidden);

        var byStars = await visible
            .GroupBy(r => r.Stars)
            .Select(g => new { Stars = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var starCounts = Enumerable.Range(Domain.Review.MinStars, Domain.Review.MaxStars - Domain.Review.MinStars + 1)
            .Select(stars => byStars.SingleOrDefault(s => s.Stars == stars)?.Count ?? 0)
            .ToList();
        var count = starCounts.Sum();
        var sum = byStars.Sum(s => s.Stars * s.Count);

        var rows = await visible
            .OrderByDescending(r => r.CreatedAt)
            .Skip(page.Skip)
            .Take(page.Take)
            .ToListAsync(cancellationToken);

        var reviews = await mapper.MapAsync(rows, cancellationToken);

        return new ProgramReviewsDto(
            new RatingSummaryDto(Ratings.Average(count, sum), count, starCounts),
            new PagedResult<ReviewDto>(page.PageIndex, page.Take, count, reviews));
    }
}

internal static class ProgramReviewsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/programs/{programId:guid}", async (
                    Guid programId, [AsParameters] PaginationRequest page, ProgramReviewsHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, page, ct)).ToHttp())
            .AllowAnonymous()
            .WithName("ListProgramReviews");
}
