using GymHub.BuildingBlocks.Privacy;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Domain;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers;

/// <summary>Profiles, avatars, follows (both ways) and trainer applications.</summary>
internal sealed class TrainersPersonalData(TrainersDbContext db, IObjectStorage storage) : IPersonalDataSource
{
    public string Section => "profile";

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken)
    {
        var memberProfile = await db.MemberProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.DisplayName, p.AvatarObjectKey, p.CreatedAt, p.UpdatedAt })
            .SingleOrDefaultAsync(cancellationToken);

        var trainerProfile = await db.Profiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.DisplayName, p.Bio, p.Specialties, p.Links, p.AvatarObjectKey, p.CreatedAt, p.UpdatedAt })
            .SingleOrDefaultAsync(cancellationToken);

        var following = await db.Follows.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.FollowedAt)
            .Select(f => new { f.TrainerId, f.FollowedAt })
            .ToListAsync(cancellationToken);

        var trainerApplications = await db.Applications.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.SubmittedAt)
            .Select(a => new { a.DisplayName, a.Bio, a.Specialties, a.Links, a.Status, a.SubmittedAt, a.ReviewedAt, a.RejectionReason })
            .ToListAsync(cancellationToken);

        return new { memberProfile, trainerProfile, following, trainerApplications };
    }

    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await storage.DeletePrefixAsync(MemberProfile.AvatarKeyPrefix(userId), cancellationToken);
        await storage.DeletePrefixAsync(TrainerProfile.AvatarKeyPrefix(userId), cancellationToken);

        await db.MemberProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Profiles.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Follows.Where(f => f.UserId == userId || f.TrainerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Applications.Where(a => a.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
