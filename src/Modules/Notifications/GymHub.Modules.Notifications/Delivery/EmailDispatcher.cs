using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Domain;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GymHub.Modules.Notifications.Delivery;

public sealed record OutgoingEmail(string To, string Subject, string TextBody, string HtmlBody);

public interface IEmailSender
{
    bool IsConfigured { get; }

    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

/// <summary>SMTP through MailKit. Locally this is Mailpit.</summary>
internal sealed class SmtpEmailSender(IOptions<NotificationsOptions> options) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.Smtp.Host);

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var smtp = options.Value.Smtp;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(smtp.FromName, smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host!, smtp.Port, smtp.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);

        if (!string.IsNullOrEmpty(smtp.UserName))
        {
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? "", cancellationToken);
        }

        await client.SendAsync(new MailKit.SendRequest(message), cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

/// <summary>Sends queued emails every few seconds, retrying failures with backoff until they give up.</summary>
internal sealed class EmailDispatcher(IServiceScopeFactory scopes, IEmailSender sender, TimeProvider timeProvider, ILogger<EmailDispatcher> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!sender.IsConfigured)
        {
            logger.LogWarning("Notifications:Smtp:Host is not set, so notification emails stay queued");
            return;
        }

        using var timer = new PeriodicTimer(Interval, timeProvider);

        do
        {
            try
            {
                await SendDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending notification emails failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass: sends the due emails. Returns how many were attempted.</summary>
    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var due = await db.Emails
            .Where(e => e.Status == EmailStatus.Pending && e.NextAttemptAt <= now)
            .OrderBy(e => e.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var email in due)
        {
            try
            {
                await sender.SendAsync(new OutgoingEmail(email.ToAddress, email.Subject, email.TextBody, email.HtmlBody), cancellationToken);
                email.MarkSent(timeProvider.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                email.MarkFailed(ex.Message, timeProvider.GetUtcNow().UtcDateTime);
                logger.LogWarning(ex, "Email {EmailId} failed (attempt {Attempt})", email.Id, email.Attempts);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }
}

/// <summary>Daily: deletes old notifications and emails.</summary>
internal sealed class NotificationCleanupJob(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<NotificationCleanupJob> logger)
    : BackgroundService
{
    public static readonly TimeSpan ReadRetention = TimeSpan.FromDays(90);
    public static readonly TimeSpan UnreadRetention = TimeSpan.FromDays(180);
    public static readonly TimeSpan FailedEmailRetention = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), timeProvider);

        do
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var readBefore = now - ReadRetention;
        var unreadBefore = now - UnreadRetention;
        var failedBefore = now - FailedEmailRetention;

        await db.Emails
            .Where(e => (e.Status == EmailStatus.Sent && e.CreatedAt < readBefore) || (e.Status == EmailStatus.Failed && e.CreatedAt < failedBefore))
            .ExecuteDeleteAsync(cancellationToken);

        await db.Notifications
            .Where(n => (n.ReadAt != null && n.CreatedAt < readBefore) || n.CreatedAt < unreadBefore)
            .Where(n => !db.Emails.Any(e => e.NotificationId == n.Id && e.Status == EmailStatus.Pending))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
