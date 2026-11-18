using GymHub.BuildingBlocks.Admin;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers;

internal sealed class TrainersMetrics(TrainersDbContext db) : IAdminMetricsSource
{
    public async Task<IReadOnlyList<AdminMetric>> GetMetricsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var waiting = await db.Applications.CountAsync(a => a.Status == ApplicationStatus.Pending, cancellationToken);

        return [new(AdminSections.Moderation, "Trainer applications waiting", waiting, Link: "/admin/trainer-applications")];
    }
}
