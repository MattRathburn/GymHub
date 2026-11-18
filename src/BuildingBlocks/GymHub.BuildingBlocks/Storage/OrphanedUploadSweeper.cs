using GymHub.BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymHub.BuildingBlocks.Storage;

/// <summary>
/// Deletes a module's stored objects that nothing references: uploads that were never attached, and media that was
/// replaced or removed. Objects younger than <see cref="GracePeriod"/> are left alone, so an upload whose attach is
/// still on its way isn't touched. Each module sweeps only its own prefixes and answers which keys it still uses.
/// </summary>
public abstract class OrphanedUploadSweeper(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger logger) : BackgroundService
{
    /// <summary>Well past any presigned upload's lifetime.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(1);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int BatchSize = 500;

    /// <summary>The key prefixes this module owns, e.g. "programs/".</summary>
    protected abstract IReadOnlyList<string> Prefixes { get; }

    /// <summary>Which of these keys the module's data still points at.</summary>
    protected abstract Task<IReadOnlySet<string>> FindReferencedAsync(
        IServiceProvider services, IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ISingletonJobLock jobLock;

        using (var scope = scopeFactory.CreateScope())
        {
            jobLock = scope.ServiceProvider.GetRequiredService<ISingletonJobLock>();
        }

        using var timer = new PeriodicTimer(Interval, timeProvider);

        do
        {
            try
            {
                await jobLock.TryRunAsync($"sweep-{GetType().Name}", SweepOnceAsync, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Sweeping unattached uploads failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Returns how many objects were deleted.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var olderThan = timeProvider.GetUtcNow().UtcDateTime - GracePeriod;
        var deleted = 0;

        foreach (var prefix in Prefixes)
        {
            var batch = new List<string>(BatchSize);

            await foreach (var listed in storage.ListAsync(prefix, cancellationToken))
            {
                if (listed.LastModified <= olderThan)
                {
                    batch.Add(listed.Key);
                }

                if (batch.Count == BatchSize)
                {
                    deleted += await DeleteUnreferencedAsync(scope.ServiceProvider, storage, batch, cancellationToken);
                    batch.Clear();
                }
            }

            deleted += await DeleteUnreferencedAsync(scope.ServiceProvider, storage, batch, cancellationToken);
        }

        if (deleted > 0)
        {
            logger.LogInformation("Deleted {Count} unattached uploads under {Prefixes}", deleted, string.Join(", ", Prefixes));
        }

        return deleted;
    }

    private async Task<int> DeleteUnreferencedAsync(IServiceProvider services, IObjectStorage storage, List<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return 0;
        }

        var referenced = await FindReferencedAsync(services, keys, cancellationToken);
        var orphans = keys.Where(k => !referenced.Contains(k)).ToList();

        foreach (var key in orphans)
        {
            await storage.DeleteAsync(key, cancellationToken);
        }

        return orphans.Count;
    }
}
