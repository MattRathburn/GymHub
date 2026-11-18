namespace GymHub.Modules.Accounts.Domain;

/// <summary>
/// An admin suspended this user: their Keycloak account is disabled and the API refuses their tokens. Reactivating
/// removes the row. Their programs, posts and reviews are left alone.
/// </summary>
public sealed class AccountSuspension
{
    public const int MaxReasonLength = 500;

    private AccountSuspension() { }

    public AccountSuspension(string userId, string suspendedBy, string? reason, DateTime now)
    {
        UserId = userId;
        SuspendedBy = suspendedBy;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SuspendedAt = now;
    }

    public string UserId { get; private set; } = "";
    public string SuspendedBy { get; private set; } = "";
    public string? Reason { get; private set; }
    public DateTime SuspendedAt { get; private set; }
}
