using GymHub.BuildingBlocks.Admin;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Programs;

internal sealed class ProgramsMetrics(ProgramsDbContext db) : IAdminMetricsSource
{
    public async Task<IReadOnlyList<AdminMetric>> GetMetricsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var byStatus = await db.Programs
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        return
        [
            new(AdminSections.Programs, "Published programs", byStatus.GetValueOrDefault(ProgramStatus.Published), Link: "/admin/programs"),
            new(AdminSections.Programs, "Drafts", byStatus.GetValueOrDefault(ProgramStatus.Draft)),
            new(AdminSections.Programs, "Archived", byStatus.GetValueOrDefault(ProgramStatus.Archived))
        ];
    }
}
