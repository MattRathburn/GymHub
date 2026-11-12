using System.Net;
using GymHub.Modules.Notifications.Domain;

namespace GymHub.Modules.Notifications.Delivery;

/// <summary>Plain text and simple HTML for a notification email: title, body, a button to the app, and a settings link.</summary>
internal static class EmailTemplates
{
    public const string SettingsPath = "me/notifications/settings";

    public static (string Subject, string Text, string Html) Render(Notification notification, Uri baseUrl)
    {
        var link = new Uri(baseUrl, notification.Link.TrimStart('/'));
        var settings = new Uri(baseUrl, SettingsPath);

        var text = $"""
            {notification.Title}

            {notification.Body}

            Open GymHub: {link}

            --
            Choose which emails you get: {settings}
            """;

        var title = WebUtility.HtmlEncode(notification.Title);
        var body = WebUtility.HtmlEncode(notification.Body);
        var href = WebUtility.HtmlEncode(link.ToString());
        var settingsHref = WebUtility.HtmlEncode(settings.ToString());

        var html = $"""
            <!doctype html>
            <html>
              <body style="font-family: system-ui, sans-serif; color: #0f172a; max-width: 560px; margin: 0 auto; padding: 24px;">
                <h1 style="font-size: 20px;">{title}</h1>
                <p style="font-size: 15px; line-height: 1.5;">{body}</p>
                <p><a href="{href}" style="display: inline-block; background: #4f46e5; color: #fff; padding: 10px 16px; border-radius: 6px; text-decoration: none;">Open GymHub</a></p>
                <hr style="border: none; border-top: 1px solid #e2e8f0; margin: 24px 0;" />
                <p style="font-size: 12px; color: #64748b;"><a href="{settingsHref}" style="color: #64748b;">Choose which emails you get</a></p>
              </body>
            </html>
            """;

        return ($"GymHub: {notification.Title}", text, html);
    }
}
