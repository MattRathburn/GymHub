using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Reviews.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews.Features.Shared;

/// <summary>
/// Runs a change that can move a program's rating (writing, deleting, hiding a review) and recomputes the rating in
/// the same transaction. A per-program advisory lock serializes these changes, so each recompute sees the ones
/// before it and versions never go backwards.
/// </summary>
internal sealed class RatingWriter(ReviewsDbContext db, TimeProvider timeProvider)
{
    /// <param name="change">Loads what it needs (the change tracker is cleared first) and stages its changes.</param>
    public Task<Result> RunAsync(Guid programId, Func<CancellationToken, Task<Result>> change, CancellationToken cancellationToken) =>
        // Aspire turns on retries, and a retrying strategy only allows a transaction it runs itself.
        db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var lockKey = $"review-rating:{programId}";
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);

            var result = await change(ct);

            if (!result.IsSuccess)
            {
                return result;
            }

            await db.SaveChangesAsync(ct);

            var totals = await db.Reviews
                .Where(r => r.ProgramId == programId && !r.IsHidden)
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Sum = g.Sum(r => r.Stars) })
                .SingleOrDefaultAsync(ct);

            var rating = await db.Ratings.SingleOrDefaultAsync(r => r.ProgramId == programId, ct);

            if (rating is null)
            {
                rating = new ProgramRating(programId);
                db.Ratings.Add(rating);
            }

            if (rating.Update(totals?.Count ?? 0, totals?.Sum ?? 0,timeProvider.GetUtcNow().UtcDateTime) is { } changed)
            {
                db.AddIntegrationEvent(changed);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
}
