using GymHub.Modules.TrainingLog.Data;
using GymHub.Modules.TrainingLog.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.TrainingLog.Features.Shared;

/// <summary>Finds the personal records a saved session set, against all of the member's other sessions.</summary>
internal sealed class SessionRecords(TrainingLogDbContext db)
{
    public async Task<IReadOnlyList<RecordDto>> ForSessionAsync(WorkoutSession session, CancellationToken cancellationToken)
    {
        var names = session.Exercises
            .GroupBy(e => e.ExerciseKey)
            .ToDictionary(g => g.Key, g => g.First().Name);
        var keys = names.Keys.ToList();

        var entries = await db.Exercises
            .AsNoTracking()
            .Where(e => e.UserId == session.UserId && keys.Contains(e.ExerciseKey))
            .Select(e => new { e.ExerciseKey, e.SessionId, e.PerformedOn, e.LoggedAt, e.TopWeightKg, e.BestE1RmKg })
            .ToListAsync(cancellationToken);

        return
        [
            .. entries
                .GroupBy(e => e.ExerciseKey)
                .SelectMany(g => PersonalRecords
                    .History(g.Select(e => new RecordEntry(e.SessionId, e.PerformedOn, e.LoggedAt, e.TopWeightKg, e.BestE1RmKg)))
                    .Where(r => r.SessionId == session.Id)
                    .Select(r => new RecordDto(g.Key, names[g.Key], r.Kind, r.Value, r.Previous)))
                .OrderBy(r => session.Exercises.First(e => e.ExerciseKey == r.ExerciseKey).Order)
                .ThenBy(r => r.Kind)
        ];
    }
}
