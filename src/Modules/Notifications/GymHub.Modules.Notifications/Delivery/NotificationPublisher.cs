using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Notifications.Delivery;

/// <summary>A notification to create for one user. DedupeKey makes it once per user, however often the event arrives.</summary>
public sealed record NotificationDraft(string UserId, NotificationType Type, string Title, string Body, string Link, string DedupeKey);

/// <summary>Pushes the unread count to users whose inbox changed.</summary>
public interface IUnreadNotifier
{
    Task UnreadChangedAsync(IReadOnlyCollection<string> userIds);
}

/// <summary>
/// Turns drafts into notifications and queued emails, applying each user's preferences. The actor (whoever caused
/// the event) is never notified, and drafts already delivered are skipped, so event handlers stay idempotent.
/// </summary>
internal sealed class NotificationPublisher(
    NotificationsDbContext db, IUnreadNotifier notifier, IOptions<NotificationsOptions> options, TimeProvider timeProvider)
{
    private const int BatchSize = 500;

    public async Task PublishAsync(IEnumerable<NotificationDraft> drafts, string? actorId = null)
    {
        var pending = drafts
            .Where(d => d.UserId != actorId && !string.IsNullOrEmpty(d.UserId))
            .DistinctBy(d => (d.UserId, d.DedupeKey))
            .ToList();

        var shownTo = new HashSet<string>();

        foreach (var batch in pending.Chunk(BatchSize))
        {
            shownTo.UnionWith(await PublishBatchAsync(batch));
        }

        if (shownTo.Count > 0)
        {
            await notifier.UnreadChangedAsync(shownTo);
        }
    }

    private async Task<IEnumerable<string>> PublishBatchAsync(NotificationDraft[] batch)
    {
        var userIds = batch.Select(d => d.UserId).Distinct().ToList();
        var keys = batch.Select(d => d.DedupeKey).Distinct().ToList();

        var delivered = (await db.Notifications
                .Where(n => userIds.Contains(n.UserId) && keys.Contains(n.DedupeKey))
                .Select(n => new { n.UserId, n.DedupeKey })
                .ToListAsync())
            .Select(n => (n.UserId, n.DedupeKey))
            .ToHashSet();

        var preferences = await db.Preferences.AsNoTracking().Where(p => userIds.Contains(p.UserId)).ToListAsync();
        var recipients = await db.Recipients.AsNoTracking().Where(r => userIds.Contains(r.UserId)).ToDictionaryAsync(r => r.UserId);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var shownTo = new List<string>();

        foreach (var draft in batch.Where(d => !delivered.Contains((d.UserId, d.DedupeKey))))
        {
            var saved = preferences.SingleOrDefault(p => p.UserId == draft.UserId && p.Type == draft.Type);
            var (inApp, email) = NotificationCatalog.Resolve(draft.Type, saved);
            var address = EmailAddress(recipients.GetValueOrDefault(draft.UserId));

            if (!inApp && (!email || address is null))
            {
                continue;
            }

            var notification = new Notification(draft.UserId, draft.Type, draft.Title, draft.Body, draft.Link, draft.DedupeKey, inApp, now);
            db.Notifications.Add(notification);

            if (inApp)
            {
                shownTo.Add(draft.UserId);
            }

            if (email && address is not null)
            {
                var (subject, text, html) = EmailTemplates.Render(notification, options.Value.PublicBaseUrl);
                db.Emails.Add(new EmailMessage(notification.Id, address, subject, text, html, now));
            }
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return shownTo;
    }

    private string? EmailAddress(Recipient? recipient) =>
        recipient is { Email: { Length: > 0 } email } && (recipient.EmailVerified || options.Value.EmailUnverifiedAddresses)
            ? email
            : null;
}
