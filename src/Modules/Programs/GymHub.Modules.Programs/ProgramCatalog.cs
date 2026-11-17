using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Programs;

internal sealed class ProgramCatalog(ProgramsDbContext db) : IProgramCatalog
{
    public async Task<ProgramSummary?> GetSummaryAsync(Guid programId, CancellationToken cancellationToken)
    {
        var summaries = await GetSummariesAsync([programId], cancellationToken);
        return summaries.GetValueOrDefault(programId);
    }

    public async Task<IReadOnlyDictionary<Guid, ProgramSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> programIds, CancellationToken cancellationToken)
    {
        var programs = await db.Programs
            .AsNoTracking()
            .Include(p => p.Prices)
            .Where(p => programIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        return programs.ToDictionary(
            p => p.Id,
            p => new ProgramSummary(p.Id, p.TrainerId, p.Title, p.Status, p.CurrentPrice.Price, p.CurrentPrice.Version));
    }

    public async Task<IReadOnlyList<string>> GetPublishingTrainerIdsAsync(CancellationToken cancellationToken) =>
        await db.Programs
            .AsNoTracking()
            .Where(p => p.Status == ProgramStatus.Published)
            .Select(p => p.TrainerId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, TrainerCatalogStats>> GetTrainerStatsAsync(
        IReadOnlyCollection<string> trainerIds, CancellationToken cancellationToken)
    {
        var trainers = await db.Programs
            .AsNoTracking()
            .Where(p => p.Status == ProgramStatus.Published && trainerIds.Contains(p.TrainerId))
            .GroupBy(p => p.TrainerId)
            .Select(g => new { TrainerId = g.Key, Count = g.Count(), RatingCount = g.Sum(p => p.RatingCount), RatingSum = g.Sum(p => p.RatingSum) })
            .ToListAsync(cancellationToken);

        return trainers.ToDictionary(t => t.TrainerId, t => new TrainerCatalogStats(t.Count, t.RatingCount, t.RatingSum));
    }
}
