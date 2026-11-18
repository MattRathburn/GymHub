using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Subscriptions.Data;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Subscriptions;

/// <summary>Enrollments are deleted: access ends with the account.</summary>
internal sealed class SubscriptionsPersonalData(SubscriptionsDbContext db) : IPersonalDataSource
{
    public string Section => "subscriptions";

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken) =>
        await db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.EnrolledAt)
            .Select(e => new { e.ProgramId, e.Status, e.Source, e.EnrolledAt, e.CancelledAt })
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Removes the enrollments, raising EnrollmentCancelled for the active ones in the same SaveChanges, so open
    /// discussion-board connections lose access as they do when a member leaves.
    /// </summary>
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        var enrollments = await db.Enrollments.Where(e => e.UserId == userId).ToListAsync(cancellationToken);

        foreach (var enrollment in enrollments.Where(e => e.IsActive))
        {
            db.AddIntegrationEvent(new EnrollmentCancelled(userId, enrollment.ProgramId));
        }

        db.Enrollments.RemoveRange(enrollments);
        await db.SaveChangesAsync(cancellationToken);
    }
}
