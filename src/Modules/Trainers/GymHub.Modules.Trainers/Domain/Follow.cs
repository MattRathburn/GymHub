using GymHub.BuildingBlocks.Results;

namespace GymHub.Modules.Trainers.Domain;

/// <summary>A user following a trainer. One row per pair; unfollowing deletes it.</summary>
public sealed class Follow
{
    private Follow() { }

    public string UserId { get; private set; } = "";
    public string TrainerId { get; private set; } = "";
    public DateTime FollowedAt { get; private set; }

    public static Result<Follow> Start(string userId, string trainerId, DateTime now)
    {
        if (userId == trainerId)
        {
            return Error.Invalid("You can't follow yourself.");
        }

        return new Follow { UserId = userId, TrainerId = trainerId, FollowedAt = now };
    }
}
