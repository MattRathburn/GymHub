namespace GymHub.Modules.Accounts.Domain;

/// <summary>
/// A member's request to delete their account. It waits out a grace period, during which they can cancel (which
/// removes the row). After the data is erased the row stays as a record that the deletion happened.
/// </summary>
public sealed class AccountDeletion
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);

    private AccountDeletion() { }

    public string UserId { get; private set; } = "";
    public DateTime RequestedAt { get; private set; }
    public DateTime ScheduledFor { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    public static AccountDeletion Schedule(string userId, DateTime now)
    {
        // Postgres keeps microseconds, so the date returned now matches the one read back later.
        now = now.AddTicks(-(now.Ticks % 10));

        return new AccountDeletion
        {
            UserId = userId,
            RequestedAt = now,
            ScheduledFor = now.Add(GracePeriod)
        };
    }

    public bool IsDue(DateTime now) => !IsCompleted && ScheduledFor <= now;

    public void Complete(DateTime now) => CompletedAt = now;
}
