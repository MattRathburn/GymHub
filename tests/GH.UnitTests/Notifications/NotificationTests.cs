using GymHub.Modules.Notifications.Delivery;
using GymHub.Modules.Notifications.Domain;

namespace GH.UnitTests.Notifications;

public class NotificationCatalogTests
{
    [Fact]
    public void Every_type_has_one_definition()
    {
        Assert.Equal(Enum.GetValues<NotificationType>().Order(), NotificationCatalog.All.Select(d => d.Type).Order());
    }

    [Theory]
    [InlineData(NotificationType.PriceChangeScheduled)]
    [InlineData(NotificationType.TrialEnding)]
    [InlineData(NotificationType.PaymentFailed)]
    [InlineData(NotificationType.AccessEnded)]
    [InlineData(NotificationType.TrainerApproved)]
    public void Money_and_access_emails_are_locked_on(NotificationType type)
    {
        var saved = new NotificationPreference("u1", type, inApp: false, email: false);

        Assert.Equal((false, true), NotificationCatalog.Resolve(type, saved));
    }

    [Fact]
    public void Defaults_apply_until_the_user_chooses()
    {
        Assert.Equal((true, false), NotificationCatalog.Resolve(NotificationType.PostReplied, null));
        Assert.Equal((true, true), NotificationCatalog.Resolve(NotificationType.WeekReleased, null));

        var saved = new NotificationPreference("u1", NotificationType.PostReplied, inApp: false, email: true);
        Assert.Equal((false, true), NotificationCatalog.Resolve(NotificationType.PostReplied, saved));
    }
}

public class EmailMessageTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Failures_back_off_then_give_up()
    {
        var email = new EmailMessage(Guid.NewGuid(), "a@b.test", "Subject", "Text", "<p>Html</p>", Now);

        email.MarkFailed("down", Now);
        Assert.Equal(Now.AddMinutes(1), email.NextAttemptAt);

        email.MarkFailed("down", Now);
        Assert.Equal(Now.AddMinutes(5), email.NextAttemptAt);

        email.MarkFailed("down", Now);
        email.MarkFailed("down", Now);
        Assert.Equal(EmailStatus.Pending, email.Status);

        email.MarkFailed("down", Now);
        Assert.Equal(EmailStatus.Failed, email.Status);
        Assert.Equal(EmailMessage.MaxAttempts, email.Attempts);
    }

    [Fact]
    public void Emails_escape_user_text_and_link_back_to_the_app()
    {
        var notification = new Notification("u1", NotificationType.ThreadStarted, "New thread", "<script>alert(1)</script>",
            "/programs/p1/discussion/t1", "thread-started:t1", showInApp: true, Now);

        var (subject, text, html) = EmailTemplates.Render(notification, new Uri("https://gymhub.test/"));

        Assert.Equal("GymHub: New thread", subject);
        Assert.Contains("https://gymhub.test/programs/p1/discussion/t1", text);
        Assert.Contains("https://gymhub.test/me/notifications/settings", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Long_text_is_cut_to_fit()
    {
        var notification = new Notification("u1", NotificationType.WeekReleased, new string('x', 500), "Body", "/", "k", true, Now);

        Assert.Equal(Notification.MaxTitleLength, notification.Title.Length);
        Assert.EndsWith("…", notification.Title);
    }
}
