using EventBus.Abstractions;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Subscriptions;

// Billing's purchase events become enrollments. Each handler is idempotent, and an event only touches the enrollment
// of its own purchase, so a late event for an old purchase can't end access that a newer purchase gives.

internal sealed class PaidAccessGrantedHandler(SubscriptionsDbContext db, TimeProvider timeProvider) : IIntegrationEventHandler<PaidAccessGranted>
{
    public async Task Handle(PaidAccessGranted @event)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var enrollment = await db.FindEnrollmentAsync(@event.UserId, @event.ProgramId);

        if (enrollment is null)
        {
            db.Enrollments.Add(Enrollment.StartPaid(@event.UserId, @event.ProgramId, @event.PurchaseId, now));
        }
        else if (enrollment.PurchaseId != @event.PurchaseId || !enrollment.IsActive)
        {
            enrollment.ActivatePaid(@event.PurchaseId, now);
        }

        await db.SaveChangesAsync();
    }
}

internal sealed class PaidAccessEndedHandler(SubscriptionsDbContext db, TimeProvider timeProvider) : IIntegrationEventHandler<PaidAccessEnded>
{
    public async Task Handle(PaidAccessEnded @event)
    {
        var enrollment = await db.FindEnrollmentAsync(@event.UserId, @event.ProgramId);

        if (enrollment is not { IsActive: true } || enrollment.PurchaseId != @event.PurchaseId)
        {
            return;
        }

        enrollment.Cancel(timeProvider.GetUtcNow().UtcDateTime);
        db.AddIntegrationEvent(new EnrollmentCancelled(@event.UserId, @event.ProgramId));
        await db.SaveChangesAsync();
    }
}

internal sealed class PaidAccessConvertedToFreeHandler(SubscriptionsDbContext db) : IIntegrationEventHandler<PaidAccessConvertedToFree>
{
    public async Task Handle(PaidAccessConvertedToFree @event)
    {
        var enrollment = await db.FindEnrollmentAsync(@event.UserId, @event.ProgramId);

        if (enrollment is not { IsActive: true } || enrollment.PurchaseId != @event.PurchaseId)
        {
            return;
        }

        enrollment.ConvertToFree();
        await db.SaveChangesAsync();
    }
}

internal static class EnrollmentLookup
{
    public static Task<Enrollment?> FindEnrollmentAsync(this SubscriptionsDbContext db, string userId, Guid programId) =>
        db.Enrollments.SingleOrDefaultAsync(e => e.UserId == userId && e.ProgramId == programId);
}
