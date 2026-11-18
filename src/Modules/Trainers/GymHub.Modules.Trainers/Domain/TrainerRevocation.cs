namespace GymHub.Modules.Trainers.Domain;

/// <summary>
/// An admin took the trainer role away. Written in the same request as the revocation, so Programs can refuse a
/// publish even while the user's token still carries the role. A new approval removes it.
/// </summary>
public sealed class TrainerRevocation
{
    private TrainerRevocation() { }

    public TrainerRevocation(string userId, string revokedBy, DateTime now)
    {
        UserId = userId;
        RevokedBy = revokedBy;
        RevokedAt = now;
    }

    public string UserId { get; private set; } = "";
    public string RevokedBy { get; private set; } = "";
    public DateTime RevokedAt { get; private set; }
}
