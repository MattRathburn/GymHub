using EventBus.Events;

namespace GymHub.Modules.Reviews.Contracts;

/// <summary>
/// A program's rating changed: Count and Sum cover its visible reviews. Version grows with every change, so a handler
/// that stores a copy keeps only the newest (delivery is at-least-once and may arrive out of order).
/// </summary>
public sealed record ProgramRatingChanged(Guid ProgramId, int Count, int Sum, long Version) : IntegrationEvent;

/// <summary>A member reviewed a program for the first time (edits don't raise it).</summary>
public sealed record ReviewPosted(Guid ReviewId, Guid ProgramId, string TrainerId, string AuthorId, int Stars) : IntegrationEvent;

public static class Ratings
{
    /// <summary>The average shown everywhere: one decimal, or null without reviews.</summary>
    public static decimal? Average(int count, int sum) =>
        count == 0 ? null : Math.Round((decimal)sum / count, 1, MidpointRounding.AwayFromZero);
}
