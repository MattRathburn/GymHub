namespace GymHub.Modules.Trainers.Domain;

/// <summary>A trainer's public profile. Keyed by the trainer's Keycloak "sub", which is also Programs' TrainerId.</summary>
public sealed class TrainerProfile
{
    private TrainerProfile() { }

    public string UserId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string? Bio { get; private set; }
    public List<string> Specialties { get; private set; } = [];
    public List<string> Links { get; private set; } = [];
    public string? AvatarObjectKey { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Avatar uploads for this trainer live under this prefix, so a trainer can only attach their own uploads.</summary>
    public static string AvatarKeyPrefix(string userId) => $"trainers/{userId}/avatar/";

    public static TrainerProfile Create(string userId, TrainerDetails details, DateTime now)
    {
        var profile = new TrainerProfile { UserId = userId, CreatedAt = now };
        profile.Update(details, now);
        return profile;
    }

    public void Update(TrainerDetails details, DateTime now)
    {
        DisplayName = details.DisplayName;
        Bio = details.Bio;
        Specialties = [.. details.Specialties];
        Links = [.. details.Links];
        UpdatedAt = now;
    }

    public void SetAvatar(string objectKey, DateTime now)
    {
        AvatarObjectKey = objectKey;
        UpdatedAt = now;
    }
}
