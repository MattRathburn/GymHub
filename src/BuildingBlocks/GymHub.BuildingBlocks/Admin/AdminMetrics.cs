using Microsoft.Extensions.DependencyInjection;

namespace GymHub.BuildingBlocks.Admin;

/// <summary>
/// One number on the admin dashboard. Money is in minor units with its <paramref name="Currency"/>;
/// <paramref name="Link"/> is the SPA page that shows the detail.
/// </summary>
public sealed record AdminMetric(string Section, string Label, long Value, string? Currency = null, string? Link = null);

public static class AdminSections
{
    public const string Users = "Users";
    public const string Programs = "Programs";
    public const string Money = "Money";
    public const string Moderation = "Moderation";
}

/// <summary>A module's numbers for the admin dashboard (collected by the Accounts module).</summary>
public interface IAdminMetricsSource
{
    Task<IReadOnlyList<AdminMetric>> GetMetricsAsync(DateTime now, CancellationToken cancellationToken);
}

public static class AdminMetricsExtensions
{
    /// <summary>How far back the dashboard's "recent" numbers look.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);

    public static IServiceCollection AddAdminMetrics<T>(this IServiceCollection services)
        where T : class, IAdminMetricsSource =>
        services.AddScoped<IAdminMetricsSource, T>();
}
