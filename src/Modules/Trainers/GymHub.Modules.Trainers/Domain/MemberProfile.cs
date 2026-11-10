using GymHub.BuildingBlocks.Results;

namespace GymHub.Modules.Trainers.Domain;

/// <summary>
/// Any user's basic public identity (display name and avatar), shown next to their posts. Keyed by the Keycloak "sub".
/// Trainers also have a <see cref="TrainerProfile"/>, whose name takes precedence.
/// </summary>
public sealed class MemberProfile
{
    public const int MinDisplayNameLength = 2;
    public const int MaxDisplayNameLength = 40;

    private MemberProfile() { }

    public string UserId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string? AvatarObjectKey { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Avatar uploads for this member live under this prefix, so a member can only attach their own uploads.</summary>
    public static string AvatarKeyPrefix(string userId) => $"members/{userId}/avatar/";

    public static Result<MemberProfile> Create(string userId, string displayName, DateTime now)
    {
        var profile = new MemberProfile { UserId = userId, CreatedAt = now };
        var renamed = profile.Rename(displayName, now);
        return renamed.IsSuccess ? profile : renamed.Error!;
    }

    public Result Rename(string displayName, DateTime now)
    {
        var name = displayName.Trim();

        if (name.Length is < MinDisplayNameLength or > MaxDisplayNameLength)
        {
            return Error.Invalid($"Display names are {MinDisplayNameLength}–{MaxDisplayNameLength} characters.");
        }

        DisplayName = name;
        UpdatedAt = now;
        return Result.Success();
    }

    public void SetAvatar(string objectKey, DateTime now)
    {
        AvatarObjectKey = objectKey;
        UpdatedAt = now;
    }
}
