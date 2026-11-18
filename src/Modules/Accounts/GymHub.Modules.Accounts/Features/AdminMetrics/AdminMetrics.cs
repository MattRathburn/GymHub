using GymHub.BuildingBlocks.Admin;
using GymHub.BuildingBlocks.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Accounts.Features.AdminMetrics;

/// <summary>The admin dashboard: every module's <see cref="IAdminMetricsSource"/>, in module order.</summary>
internal sealed class AdminMetricsHandler(IEnumerable<IAdminMetricsSource> sources, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<AdminMetric>> Handle(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var metrics = new List<AdminMetric>();

        foreach (var source in sources)
        {
            metrics.AddRange(await source.GetMetricsAsync(now, cancellationToken));
        }

        return metrics;
    }
}

internal static class AdminMetricsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/admin/metrics", async (AdminMetricsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(ct)))
            .RequireAuthorization(Policies.Admin)
            .WithName("AdminMetrics");
}
