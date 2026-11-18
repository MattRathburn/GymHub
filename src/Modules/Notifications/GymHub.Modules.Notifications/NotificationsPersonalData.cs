using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Notifications;

/// <summary>Notifications, preferences, the stored email address and every email sent or queued for the user.</summary>
internal sealed class NotificationsPersonalData(NotificationsDbContext db, IOptions<NotificationsOptions> options) : IPersonalDataSource
{
    public string Section => "notifications";

    public async Task<object> ExportAsync(string userId, CancellationToken cancellationToken)
    {
        var email = await db.Recipients.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new { r.Email, r.EmailVerified })
            .SingleOrDefaultAsync(cancellationToken);

        var preferences = await db.Preferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Type, p.InApp, p.Email })
            .ToListAsync(cancellationToken);

        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new { n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt })
            .ToListAsync(cancellationToken);

        return new { email, preferences, notifications };
    }

    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        var notificationIds = db.Notifications.Where(n => n.UserId == userId).Select(n => n.Id);
        await db.Emails.Where(e => notificationIds.Contains(e.NotificationId)).ExecuteDeleteAsync(cancellationToken);
        await db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Preferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Recipients.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        await ForgetAsActorAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Other people's notifications that name the user ("Alex replied to your post") get "Deleted user" instead, and
    /// their emails are re-rendered. In batches; a run that stops part-way picks up the rest next time.
    /// </summary>
    private async Task ForgetAsActorAsync(string userId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var notifications = await db.Notifications.Where(n => n.ActorId == userId).Take(500).ToListAsync(cancellationToken);

            if (notifications.Count == 0)
            {
                return;
            }

            var ids = notifications.Select(n => n.Id).ToList();
            var emails = await db.Emails.Where(e => ids.Contains(e.NotificationId)).ToListAsync(cancellationToken);

            foreach (var notification in notifications)
            {
                notification.ForgetActor(DeletedUser.DisplayName);
            }

            foreach (var email in emails)
            {
                var (subject, text, html) = EmailTemplates.Render(notifications.Single(n => n.Id == email.NotificationId), options.Value.PublicBaseUrl);
                email.Rewrite(subject, text, html);
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}
