namespace GymHub.Modules.Trainers.Contracts;

/// <summary>Who follows a trainer, for news about their programs.</summary>
public interface IFollowers
{
    Task<IReadOnlyList<string>> GetFollowerIdsAsync(string trainerId, CancellationToken cancellationToken);
}
