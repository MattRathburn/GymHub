using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Subscriptions;

internal sealed class Entitlements(SubscriptionsDbContext db) : IEntitlements
{
    public Task<bool> HasAccessAsync(string userId, Guid programId, CancellationToken cancellationToken) =>
        db.Enrollments.AnyAsync(
            e => e.UserId == userId && e.ProgramId == programId && e.Status == EnrollmentStatus.Active,
            cancellationToken);
}

internal sealed class ProgramMembers(SubscriptionsDbContext db) : IProgramMembers
{
    public async Task<IReadOnlyList<string>> GetActiveMemberIdsAsync(Guid programId, CancellationToken cancellationToken) =>
        await db.Enrollments
            .Where(e => e.ProgramId == programId && e.Status == EnrollmentStatus.Active)
            .Select(e => e.UserId)
            .ToListAsync(cancellationToken);
}
