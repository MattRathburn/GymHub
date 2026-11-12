using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Delivery;
using GymHub.Modules.Notifications.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Notifications.Features.Inbox;

public sealed record NotificationDto(Guid Id, NotificationType Type, string Title, string Body, string Link, DateTime CreatedAt, bool IsRead);

public sealed record UnreadCountDto(int Count);

public sealed record InboxQuery(bool UnreadOnly = false, int PageIndex = 0, int PageSize = 20);

/// <summary>The signed-in user's notifications: list, unread count and marking read.</summary>
internal sealed class InboxHandler(NotificationsDbContext db, IUnreadNotifier notifier, TimeProvider timeProvider)
{
    public async Task<PagedResult<NotificationDto>> List(CurrentUser user, InboxQuery query, CancellationToken cancellationToken)
    {
        var paging = new PaginationRequest(query.PageSize, query.PageIndex);
        var notifications = Mine(user);

        if (query.UnreadOnly)
        {
            notifications = notifications.Where(n => n.ReadAt == null);
        }

        var count = await notifications.LongCountAsync(cancellationToken);
        var page = await notifications
            .OrderByDescending(n => n.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.Take)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>(paging.PageIndex, paging.Take, count, page);
    }

    /// <summary>Also remembers the user's email address from their token, for notification emails.</summary>
    public async Task<UnreadCountDto> UnreadCount(CurrentUser user, string? email, bool emailVerified, CancellationToken cancellationToken)
    {
        var recipient = await db.Recipients.SingleOrDefaultAsync(r => r.UserId == user.Id, cancellationToken);

        if (recipient is null)
        {
            recipient = new Recipient(user.Id);
            db.Recipients.Add(recipient);
        }

        if (recipient.Update(email, emailVerified, timeProvider.GetUtcNow().UtcDateTime))
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Two tabs loading at once; the other request stored it.
            }
        }

        return new UnreadCountDto(await Mine(user).CountAsync(n => n.ReadAt == null, cancellationToken));
    }

    public async Task<Result> MarkRead(Guid id, CurrentUser user, CancellationToken cancellationToken)
    {
        var notification = await Mine(user).SingleOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (notification is null)
        {
            return Error.NotFound("Notification not found.");
        }

        if (!notification.IsRead)
        {
            notification.MarkRead(timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            await notifier.UnreadChangedAsync([user.Id]);
        }

        return Result.Success();
    }

    public async Task MarkAllRead(CurrentUser user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = await Mine(user)
            .Where(n => n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);

        if (changed > 0)
        {
            await notifier.UnreadChangedAsync([user.Id]);
        }
    }

    private IQueryable<Notification> Mine(CurrentUser user) =>
        db.Notifications.Where(n => n.UserId == user.Id && n.ShowInApp);
}

internal static class InboxEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var inbox = app.MapGroup("").RequireAuthorization();

        inbox.MapGet("/", async ([AsParameters] InboxQuery query, ClaimsPrincipal user, InboxHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.List(CurrentUser.Required(user), query, ct)))
            .WithName("ListNotifications");

        inbox.MapGet("/unread-count", async (ClaimsPrincipal user, InboxHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.UnreadCount(
                    CurrentUser.Required(user),
                    user.FindFirstValue("email"),
                    string.Equals(user.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase),
                    ct)))
            .WithName("GetUnreadNotificationCount");

        inbox.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal user, InboxHandler handler, CancellationToken ct) =>
                (await handler.MarkRead(id, CurrentUser.Required(user), ct)).ToHttp())
            .WithName("MarkNotificationRead");

        inbox.MapPost("/read-all", async (ClaimsPrincipal user, InboxHandler handler, CancellationToken ct) =>
            {
                await handler.MarkAllRead(CurrentUser.Required(user), ct);
                return TypedResults.NoContent();
            })
            .WithName("MarkAllNotificationsRead");
    }
}
