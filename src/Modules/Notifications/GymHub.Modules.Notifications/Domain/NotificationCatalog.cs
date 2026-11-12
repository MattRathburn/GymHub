namespace GymHub.Modules.Notifications.Domain;

public enum NotificationType
{
    WeekReleased,
    PostReplied,
    AnswerMarked,
    ThreadStarted,
    TrainerPublished,
    NewMember,
    TrainerApproved,
    PriceChangeScheduled,
    TrialEnding,
    PaymentFailed,
    AccessEnded
}

public enum NotificationGroup
{
    Training,
    Discussions,
    Billing,
    Trainer
}

/// <summary>What a type is called and how it is delivered unless the user says otherwise.</summary>
/// <param name="EmailLocked">Money and access messages always go by email.</param>
public sealed record NotificationDefinition(
    NotificationType Type, NotificationGroup Group, string Label, bool DefaultInApp, bool DefaultEmail, bool EmailLocked);

public static class NotificationCatalog
{
    public static IReadOnlyList<NotificationDefinition> All { get; } =
    [
        new(NotificationType.WeekReleased, NotificationGroup.Training, "A new week of your program is released", true, true, false),
        new(NotificationType.TrainerPublished, NotificationGroup.Training, "A trainer you follow publishes a program", true, true, false),
        new(NotificationType.PostReplied, NotificationGroup.Discussions, "Someone replies to your post", true, false, false),
        new(NotificationType.AnswerMarked, NotificationGroup.Discussions, "Your reply is marked as the answer", true, false, false),
        new(NotificationType.ThreadStarted, NotificationGroup.Discussions, "A new thread on your program's board", true, true, false),
        new(NotificationType.PriceChangeScheduled, NotificationGroup.Billing, "Your subscription price changes at renewal", true, true, true),
        new(NotificationType.TrialEnding, NotificationGroup.Billing, "Your free trial ends soon", true, true, true),
        new(NotificationType.PaymentFailed, NotificationGroup.Billing, "A subscription payment failed", true, true, true),
        new(NotificationType.AccessEnded, NotificationGroup.Billing, "Your access to a program ended", true, true, true),
        new(NotificationType.NewMember, NotificationGroup.Trainer, "Someone joins or buys your program", true, false, false),
        new(NotificationType.TrainerApproved, NotificationGroup.Trainer, "Your trainer application is approved", true, true, true)
    ];

    private static readonly Dictionary<NotificationType, NotificationDefinition> ByType = All.ToDictionary(d => d.Type);

    public static NotificationDefinition Get(NotificationType type) => ByType[type];

    /// <summary>How a type reaches a user, given their saved choice for it (if any).</summary>
    public static (bool InApp, bool Email) Resolve(NotificationType type, NotificationPreference? saved)
    {
        var definition = Get(type);
        var inApp = saved?.InApp ?? definition.DefaultInApp;
        var email = definition.EmailLocked || (saved?.Email ?? definition.DefaultEmail);
        return (inApp, email);
    }
}
