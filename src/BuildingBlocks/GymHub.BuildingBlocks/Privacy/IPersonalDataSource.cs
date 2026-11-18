using GymHub.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GymHub.BuildingBlocks.Privacy;

/// <summary>
/// One module's part of a user's personal data, for the data export and account deletion (Accounts module).
/// Each module that stores user data registers one with <see cref="PersonalDataExtensions.AddPersonalDataSource{T}"/>.
/// </summary>
public interface IPersonalDataSource
{
    /// <summary>The name of this module's section in the export, e.g. "trainingLog".</summary>
    string Section { get; }

    /// <summary>Everything this module stores about the user, serialized as JSON in the export.</summary>
    Task<object> ExportAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Why the account can't be deleted yet (for example, a subscription still renewing), or null.</summary>
    Task<string?> GetDeletionBlockerAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <summary>Deletes or anonymizes the user's data. Runs again after a failure, so it must be idempotent.</summary>
    Task EraseAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// Users whose account deletion has finished (implemented by the Accounts module). Their sign-ins are refused and
/// nothing new is stored for them, so erased data doesn't come back through a still-open session or a late event.
/// </summary>
public interface IDeletedAccounts
{
    Task<bool> IsDeletedAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The deleted ones among these users.</summary>
    Task<IReadOnlySet<string>> FindDeletedAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}

public static class PersonalDataExtensions
{
    public static IServiceCollection AddPersonalDataSource<T>(this IServiceCollection services)
        where T : class, IPersonalDataSource =>
        services.AddScoped<IPersonalDataSource, T>();

    /// <summary>
    /// Answers 401 to requests from a deleted account. Its token stays valid until it expires, and a still-open tab
    /// would otherwise store data again (the notification bell saves the email address on every poll).
    /// </summary>
    public static IApplicationBuilder UseDeletedAccountGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.GetUserId() is { Length: > 0 } userId
                && context.RequestServices.GetService<IDeletedAccounts>() is { } deleted
                && await deleted.IsDeletedAsync(userId, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
}

/// <summary>The name that replaces a deleted user's name on content that stays, such as posts and reviews.</summary>
public static class DeletedUser
{
    public const string DisplayName = "Deleted user";
}
