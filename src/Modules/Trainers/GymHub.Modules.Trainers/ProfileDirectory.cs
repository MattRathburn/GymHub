using GymHub.BuildingBlocks.Persistence;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Trainers.Contracts;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers;

internal sealed class ProfileDirectory(TrainersDbContext db, IObjectStorage storage) : IProfileDirectory, ITrainerDirectory
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

    public async Task<IReadOnlyList<string>> FindTrainerIdsAsync(string nameContains, CancellationToken cancellationToken)
    {
        var pattern = LikePattern.Contains(nameContains);

        return await db.Profiles.AsNoTracking()
            .Where(p => EF.Functions.ILike(p.DisplayName, pattern, LikePattern.EscapeCharacter))
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> IsRevokedAsync(string userId, CancellationToken cancellationToken) =>
        db.Revocations.AnyAsync(r => r.UserId == userId, cancellationToken);
}

internal sealed class Followers(TrainersDbContext db) : IFollowers
{
    public async Task<IReadOnlyList<string>> GetFollowerIdsAsync(string trainerId, CancellationToken cancellationToken) =>
        await db.Follows.Where(f => f.TrainerId == trainerId).Select(f => f.UserId).ToListAsync(cancellationToken);
}
