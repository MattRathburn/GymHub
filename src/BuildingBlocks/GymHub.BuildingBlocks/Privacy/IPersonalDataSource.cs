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

public static class PersonalDataExtensions
{
    public static IServiceCollection AddPersonalDataSource<T>(this IServiceCollection services)
        where T : class, IPersonalDataSource =>
        services.AddScoped<IPersonalDataSource, T>();
}

/// <summary>The name that replaces a deleted user's name on content that stays, such as posts and reviews.</summary>
public static class DeletedUser
{
    public const string DisplayName = "Deleted user";
}
