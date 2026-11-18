using GymHub.BuildingBlocks.Privacy;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Programs;

/// <summary>
/// A trainer's programs. Archived ones stay after the account is deleted so members who bought them keep access,
/// which is why published programs must be archived first. Drafts are deleted.
/// </summary>
internal sealed class ProgramsPersonalData(ProgramsDbContext db, IObjectStorage storage) : IPersonalDataSource
{
    public string Section => "programs";

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken) =>
        await db.Programs.AsNoTracking()
            .Where(p => p.TrainerId == userId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Title, p.Status, p.CreatedAt, p.PublishedAt })
            .ToListAsync(cancellationToken);

    public async Task<string?> GetDeletionBlockerAsync(string userId, CancellationToken cancellationToken)
    {
        var published = await db.Programs.CountAsync(p => p.TrainerId == userId && p.Status == ProgramStatus.Published, cancellationToken);

        return published switch
        {
            0 => null,
            1 => "You have a published program. Archive it in the trainer studio first.",
            _ => $"You have {published} published programs. Archive them in the trainer studio first."
        };
    }

    /// <summary>
    /// Drafts were never published, so nobody else has them: they go, with their media. Archived programs stay.
    /// Storage first, so a failure part-way leaves the rows (and their prefixes) for the next run.
    /// </summary>
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        var drafts = await db.Programs
            .Where(p => p.TrainerId == userId && p.Status == ProgramStatus.Draft)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var programId in drafts)
        {
            await storage.DeletePrefixAsync(TrainingProgram.MediaKeyPrefixFor(programId), cancellationToken);
        }

        // Weeks, prices and media rows go by cascade.
        await db.Programs.Where(p => drafts.Contains(p.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
