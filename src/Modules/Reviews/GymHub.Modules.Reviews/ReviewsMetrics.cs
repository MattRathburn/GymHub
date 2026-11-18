using GymHub.BuildingBlocks.Admin;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Reviews.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Reviews;

internal sealed class ReviewsMetrics(ReviewsDbContext db) : IAdminMetricsSource
{
    public async Task<IReadOnlyList<AdminMetric>> GetMetricsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var open = await db.Reports.CountAsync(r => r.Status == ReportStatus.Open, cancellationToken);

        return [new(AdminSections.Moderation, "Open review reports", open, Link: "/admin/reports?tab=reviews")];
    }
}
