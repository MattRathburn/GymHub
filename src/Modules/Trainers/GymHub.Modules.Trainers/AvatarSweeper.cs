using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Trainers.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Trainers;

/// <summary>Avatar uploads that no trainer or member profile points at (never attached, or replaced).</summary>
internal sealed class AvatarSweeper(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<AvatarSweeper> logger)
    : OrphanedUploadSweeper(scopeFactory, timeProvider, logger)
{
    protected override IReadOnlyList<string> Prefixes { get; } = ["trainers/", "members/"];

    protected override async Task<IReadOnlySet<string>> FindReferencedAsync(
        IServiceProvider services, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TrainersDbContext>();

        var trainers = await db.Profiles.AsNoTracking()
            .Where(p => p.AvatarObjectKey != null && keys.Contains(p.AvatarObjectKey))
            .Select(p => p.AvatarObjectKey!)
            .ToListAsync(cancellationToken);

        var members = await db.MemberProfiles.AsNoTracking()
            .Where(p => p.AvatarObjectKey != null && keys.Contains(p.AvatarObjectKey))
            .Select(p => p.AvatarObjectKey!)
            .ToListAsync(cancellationToken);

        return trainers.Concat(members).ToHashSet();
    }
}
