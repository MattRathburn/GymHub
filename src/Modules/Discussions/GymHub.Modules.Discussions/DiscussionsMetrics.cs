using GymHub.BuildingBlocks.Admin;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Discussions;

internal sealed class DiscussionsMetrics(DiscussionsDbContext db) : IAdminMetricsSource
{
    public async Task<IReadOnlyList<AdminMetric>> GetMetricsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var open = await db.Reports.CountAsync(r => r.Status == ReportStatus.Open, cancellationToken);

        return [new(AdminSections.Moderation, "Open post reports", open, Link: "/admin/reports")];
    }
}
