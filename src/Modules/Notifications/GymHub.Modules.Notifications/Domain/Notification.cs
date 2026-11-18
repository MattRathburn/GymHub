namespace GymHub.Modules.Notifications.Domain;

/// <summary>
/// One message to one user. Kept even when the user turned the type off in-app (ShowInApp = false), so its dedupe
/// key still stops a redelivered event from emailing twice.
/// </summary>
public sealed class Notification
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 1000;

    private Notification() { }

    public Notification(
        string userId, NotificationType type, string title, string body, string link, string dedupeKey, bool showInApp, DateTime now,
        string? actorId = null, string? actorName = null)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        ActorId = actorId;
        ActorName = actorName is null ? null : Truncate(actorName, MaxTitleLength);
        Type = type;
        Title = Truncate(title, MaxTitleLength);
        Body = Truncate(body, MaxBodyLength);
        Link = link;
        DedupeKey = dedupeKey;
        ShowInApp = showInApp;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = "";
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";

    /// <summary>An app path such as /programs/{id}.</summary>
    public string Link { get; private set; } = "";

    public string DedupeKey { get; private set; } = "";
    public bool ShowInApp { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    /// <summary>Who caused it, when someone did. Kept so the actor's name can be removed if they delete their account.</summary>
    public string? ActorId { get; private set; }

    /// <summary>The actor's name as written at the start of the title or body.</summary>
    public string? ActorName { get; private set; }

    public bool IsRead => ReadAt is not null;

    public void MarkRead(DateTime now) => ReadAt ??= now;

    /// <summary>The actor deleted their account: their name, where it leads the title or body, becomes "Deleted user".</summary>
    public void ForgetActor(string replacement)
    {
        if (!string.IsNullOrEmpty(ActorName))
        {
            Title = Truncate(ReplaceLeading(Title, ActorName, replacement), MaxTitleLength);
            Body = Truncate(ReplaceLeading(Body, ActorName, replacement), MaxBodyLength);
        }

        ActorId = null;
        ActorName = null;
    }

    private static string ReplaceLeading(string text, string name, string replacement) =>
        text.StartsWith(name, StringComparison.Ordinal) ? replacement + text[name.Length..] : text;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>A user's choice for one type. Missing means the catalog defaults.</summary>
public sealed class NotificationPreference
{
    private NotificationPreference() { }

    public NotificationPreference(string userId, NotificationType type, bool inApp, bool email)
    {
        UserId = userId;
        Type = type;
        InApp = inApp;
        Email = email;
    }

    public string UserId { get; private set; } = "";
    public NotificationType Type { get; private set; }
    public bool InApp { get; private set; }
    public bool Email { get; private set; }

    public void Set(bool inApp, bool email)
    {
        InApp = inApp;
        Email = email;
    }
}

/// <summary>Where a user's email goes, taken from their token claims when they use the app.</summary>
public sealed class Recipient
{
    private Recipient() { }

    public Recipient(string userId) => UserId = userId;

    public string UserId { get; private set; } = "";
    public string? Email { get; private set; }
    public bool EmailVerified { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>True when something changed.</summary>
    public bool Update(string? email, bool verified, DateTime now)
    {
        if (Email == email && EmailVerified == verified)
        {
            return false;
        }

        Email = email;
        EmailVerified = verified;
        UpdatedAt = now;
        return true;
    }
}

public enum EmailStatus
{
    Pending,
    Sent,
    Failed
}

/// <summary>An email waiting to be sent (or sent), queued in the same SaveChanges as its notification.</summary>
public sealed class EmailMessage
{
    public const int MaxAttempts = 5;

    /// <summary>Wait after the 1st, 2nd, 3rd and 4th failed attempt.</summary>
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

    private EmailMessage() { }

    public EmailMessage(Guid notificationId, string toAddress, string subject, string textBody, string htmlBody, DateTime now)
    {
        Id = Guid.CreateVersion7();
        NotificationId = notificationId;
        ToAddress = toAddress;
        Subject = subject;
        TextBody = textBody;
        HtmlBody = htmlBody;
        Status = EmailStatus.Pending;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public string ToAddress { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string TextBody { get; private set; } = "";
    public string HtmlBody { get; private set; } = "";
    public EmailStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Replaces the content after its notification changed (a deleted actor's name removed).</summary>
    public void Rewrite(string subject, string textBody, string htmlBody)
    {
        Subject = subject;
        TextBody = textBody;
        HtmlBody = htmlBody;
    }

    public void MarkSent(DateTime now)
    {
        Attempts++;
        Status = EmailStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    public void MarkFailed(string error, DateTime now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;

        if (Attempts >= MaxAttempts)
        {
            Status = EmailStatus.Failed;
            return;
        }

        NextAttemptAt = now + Backoff[Attempts - 1];
    }
}
