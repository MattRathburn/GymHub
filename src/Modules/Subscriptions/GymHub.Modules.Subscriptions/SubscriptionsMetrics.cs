using GymHub.BuildingBlocks.Admin;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Subscriptions;

internal sealed class SubscriptionsMetrics(SubscriptionsDbContext db) : IAdminMetricsSource
{
    public async Task<IReadOnlyList<AdminMetric>> GetMetricsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var since = now - AdminMetricsExtensions.RecentWindow;
        var active = await db.Enrollments.CountAsync(e => e.Status == EnrollmentStatus.Active, cancellationToken);
        var recent = await db.Enrollments.CountAsync(e => e.EnrolledAt >= since, cancellationToken);

        return
        [
            new(AdminSections.Programs, "Active enrollments", active),
            new(AdminSections.Programs, "New enrollments (30 days)", recent)
        ];
    }
}
