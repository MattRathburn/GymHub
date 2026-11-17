using GymHub.Modules.Reviews.Contracts;

namespace GymHub.Modules.Reviews.Domain;

/// <summary>
/// The count and sum of a program's visible reviews. It is recomputed under a per-program lock whenever a review is
/// written, deleted, hidden or shown, and every change bumps Version for <see cref="ProgramRatingChanged"/>.
/// </summary>
public sealed class ProgramRating
{
    private ProgramRating() { }

    public ProgramRating(Guid programId)
    {
        ProgramId = programId;
    }

    public Guid ProgramId { get; private set; }
    public int Count { get; private set; }
    public int Sum { get; private set; }
    public long Version { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public decimal? Average => Ratings.Average(Count, Sum);

    /// <summary>Stores new totals; returns the event to publish, or null when nothing changed.</summary>
    public ProgramRatingChanged? Update(int count, int sum, DateTime now)
    {
        if (count == Count && sum == Sum && Version > 0)
        {
            return null;
        }

        Count = count;
        Sum = sum;
        Version++;
        UpdatedAt = now;
        return new ProgramRatingChanged(ProgramId, Count, Sum, Version);
    }
}
