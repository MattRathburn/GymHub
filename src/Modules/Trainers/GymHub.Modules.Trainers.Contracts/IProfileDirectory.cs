namespace GymHub.Modules.Trainers.Contracts;

/// <summary>A user's public name and avatar. A trainer's profile name wins over their member profile name.</summary>
public sealed record AuthorInfo(string UserId, string DisplayName, Uri? AvatarUrl);

/// <summary>Synchronous lookups of users' public names for other modules (for example, post authors).</summary>
public interface IProfileDirectory
{
    /// <summary>Returns an entry only for users who have a trainer or member profile.</summary>
    Task<IReadOnlyDictionary<string, AuthorInfo>> GetAuthorsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}
