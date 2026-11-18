using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Programs;

/// <summary>Program uploads that no cover or media attachment points at (never attached, replaced or removed).</summary>
internal sealed class ProgramMediaSweeper(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ProgramMediaSweeper> logger)
    : OrphanedUploadSweeper(scopeFactory, timeProvider, logger)
{
    protected override IReadOnlyList<string> Prefixes { get; } = ["programs/"];

    protected override async Task<IReadOnlySet<string>> FindReferencedAsync(
        IServiceProvider services, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ProgramsDbContext>();

        var covers = await db.Programs.AsNoTracking()
            .Where(p => p.CoverObjectKey != null && keys.Contains(p.CoverObjectKey))
            .Select(p => p.CoverObjectKey!)
            .ToListAsync(cancellationToken);

        var media = await db.Set<MediaAttachment>().AsNoTracking()
            .Where(m => keys.Contains(m.ObjectKey))
            .Select(m => m.ObjectKey)
            .ToListAsync(cancellationToken);

        return covers.Concat(media).ToHashSet();
    }
}
