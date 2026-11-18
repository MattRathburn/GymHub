using GymHub.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GymHub.BuildingBlocks.Auth;

// The account states that lock a user out, both implemented by the Accounts module and read by UseAccountGuard.

/// <summary>
/// Users whose account deletion has finished. Their sign-ins are refused and nothing new is stored for them, so
/// erased data doesn't come back through a still-open session or a late event.
/// </summary>
public interface IDeletedAccounts
{
    Task<bool> IsDeletedAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The deleted ones among these users.</summary>
    Task<IReadOnlySet<string>> FindDeletedAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}

/// <summary>Users an admin has suspended.</summary>
public interface ISuspendedAccounts
{
    Task<bool> IsSuspendedAsync(string userId, CancellationToken cancellationToken);
}

public static class AccountGuardExtensions
{
    /// <summary>
    /// Answers 401 to requests from a deleted or suspended account. Their token stays valid until it expires, and a
    /// still-open tab would otherwise keep working (or store data again: the notification bell saves the email
    /// address on every poll).
    /// </summary>
    public static IApplicationBuilder UseAccountGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.GetUserId() is { Length: > 0 } userId && await IsLockedOutAsync(context, userId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });

    private static async Task<bool> IsLockedOutAsync(HttpContext context, string userId) =>
        (context.RequestServices.GetService<IDeletedAccounts>() is { } deleted && await deleted.IsDeletedAsync(userId, context.RequestAborted))
        || (context.RequestServices.GetService<ISuspendedAccounts>() is { } suspended && await suspended.IsSuspendedAsync(userId, context.RequestAborted));
}
