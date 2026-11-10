using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Trainers.Contracts;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers;

internal sealed class ProfileDirectory(TrainersDbContext db, IObjectStorage storage) : IProfileDirectory
{
    public async Task<IReadOnlyDictionary<string, AuthorInfo>> GetAuthorsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, AuthorInfo>();
        }

        var trainers = await db.Profiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarObjectKey })
            .ToListAsync(cancellationToken);

        var members = await db.MemberProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarObjectKey })
            .ToListAsync(cancellationToken);

        var authors = members.ToDictionary(m => m.UserId, m => new AuthorInfo(m.UserId, m.DisplayName, storage.AvatarUrl(m.AvatarObjectKey)));

        foreach (var trainer in trainers)
        {
            var avatarKey = trainer.AvatarObjectKey ?? members.FirstOrDefault(m => m.UserId == trainer.UserId)?.AvatarObjectKey;
            authors[trainer.UserId] = new AuthorInfo(trainer.UserId, trainer.DisplayName, storage.AvatarUrl(avatarKey));
        }

        return authors;
    }
}
