using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Notifications.Realtime;

/// <summary>
/// Live unread counts. No groups: messages go to Clients.User(sub). They carry only the count; clients refetch the
/// inbox over REST.
/// </summary>
[Authorize]
internal sealed class NotificationsHub : Hub;

/// <summary>SignalR's user id is the Keycloak "sub", like everywhere else (inbound claims aren't mapped).</summary>
internal sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst("sub")?.Value;
}

internal sealed class HubUnreadNotifier(IHubContext<NotificationsHub> hub, IServiceScopeFactory scopes, ILogger<HubUnreadNotifier> logger)
    : IUnreadNotifier
{
    /// <summary>Client method name. Argument: the unread count.</summary>
    public const string NotificationsChanged = "notificationsChanged";

    public async Task UnreadChangedAsync(IReadOnlyCollection<string> userIds)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            var counts = await db.Notifications
                .Where(n => userIds.Contains(n.UserId) && n.ShowInApp && n.ReadAt == null)
                .GroupBy(n => n.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.UserId, g => g.Count);

            foreach (var userId in userIds)
            {
                await hub.Clients.User(userId).SendAsync(NotificationsChanged, counts.GetValueOrDefault(userId));
            }
        }
        catch (Exception ex)
        {
            // The notifications are saved; live updates are best effort.
            logger.LogWarning(ex, "Couldn't push unread counts to {Count} users", userIds.Count);
        }
    }
}
