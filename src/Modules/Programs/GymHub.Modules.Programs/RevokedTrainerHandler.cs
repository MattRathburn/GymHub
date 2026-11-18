using EventBus.Abstractions;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Trainers.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Programs;

/// <summary>
/// Archives a revoked trainer's published programs: they leave the catalog and renewals stop, and buyers keep
/// access. Drafts stay; without the role the trainer can't publish them. A second delivery finds nothing left.
/// </summary>
internal sealed class RevokedTrainerHandler(ProgramsDbContext db) : IIntegrationEventHandler<TrainerRevoked>
{
    public async Task Handle(TrainerRevoked @event)
    {
        var published = await db.Programs
            .Where(p => p.TrainerId == @event.UserId && p.Status == ProgramStatus.Published)
            .ToListAsync();

        foreach (var program in published)
        {
            program.Archive();
        }

        await db.SaveChangesAsync();
    }
}
