using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.TrainingLog.Data;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.TrainingLog;

/// <summary>Logged workouts are private to the member, so deletion removes them.</summary>
internal sealed class TrainingLogPersonalData(TrainingLogDbContext db) : IPersonalDataSource
{
    public string Section => "trainingLog";

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken)
    {
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Include(s => s.Exercises)
            .OrderBy(s => s.PerformedOn)
            .ToListAsync(cancellationToken);

        return sessions.Select(s => new
        {
            s.Id, s.PerformedOn, s.Title, s.Notes, s.DurationMin, s.ProgramId, s.ProgramTitle, s.WeekNumber, s.CreatedAt, s.UpdatedAt,
            Exercises = s.Exercises.OrderBy(e => e.Order).Select(e => new { e.Name, e.Notes, e.Sets })
        }).ToList();
    }

    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await db.Exercises.Where(e => e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
