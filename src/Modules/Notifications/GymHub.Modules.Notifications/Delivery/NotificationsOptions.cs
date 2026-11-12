namespace GymHub.Modules.Notifications.Delivery;

public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    /// <summary>The browser-facing origin (the Gateway), for links in emails.</summary>
    public Uri PublicBaseUrl { get; set; } = new("https://localhost:7179");

    /// <summary>Email addresses Keycloak hasn't verified too. On for local dev, where Mailpit catches everything.</summary>
    public bool EmailUnverifiedAddresses { get; set; }

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    /// <summary>Empty turns email off: messages stay queued until a host is configured.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 25;
    public bool UseTls { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "notifications@gymhub.local";
    public string FromName { get; set; } = "GymHub";
}
