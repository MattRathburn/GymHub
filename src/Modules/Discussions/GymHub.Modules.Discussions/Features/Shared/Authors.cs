using System.Security.Claims;
using GymHub.Modules.Trainers.Contracts;

namespace GymHub.Modules.Discussions.Features.Shared;

/// <summary>A post's author as shown on the board. IsTrainer marks the program's own trainer.</summary>
public sealed record AuthorDto(string UserId, string DisplayName, Uri? AvatarUrl, bool IsTrainer);

/// <summary>Resolves authors' current profile names, falling back to the username stored on the post.</summary>
internal sealed class AuthorLookup(IProfileDirectory directory)
{
    public async Task<Func<string, string, AuthorDto>> LoadAsync(IEnumerable<string> userIds, string trainerId, CancellationToken cancellationToken)
    {
        var profiles = await directory.GetAuthorsAsync([.. userIds.Distinct()], cancellationToken);

        return (userId, snapshotName) => profiles.TryGetValue(userId, out var profile)
            ? new AuthorDto(userId, profile.DisplayName, profile.AvatarUrl, userId == trainerId)
            : new AuthorDto(userId, snapshotName, null, userId == trainerId);
    }

    /// <summary>The caller's username, stored on their posts in case they never set up a profile.</summary>
    public static string SnapshotName(ClaimsPrincipal user) =>
        user.FindFirst("preferred_username")?.Value ?? user.Identity?.Name ?? "Member";
}
