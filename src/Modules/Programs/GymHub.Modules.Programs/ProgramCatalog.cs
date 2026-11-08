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
}
