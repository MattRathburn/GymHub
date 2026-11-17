using EventBus.Abstractions;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Reviews.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Programs;

/// <summary>
/// Keeps the catalog's copy of a program's rating. The update only applies over an older version, in one statement,
/// so repeated or out-of-order deliveries can't move it backwards.
/// </summary>
internal sealed class ProgramRatingHandler(ProgramsDbContext db) : IIntegrationEventHandler<ProgramRatingChanged>
{
    public Task Handle(ProgramRatingChanged @event) =>
        db.Programs
            .Where(p => p.Id == @event.ProgramId && p.RatingVersion < @event.Version)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.RatingCount, @event.Count)
                .SetProperty(p => p.RatingSum, @event.Sum)
                .SetProperty(p => p.RatingVersion, @event.Version));
}
