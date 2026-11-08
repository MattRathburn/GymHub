using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Programs;

/// <summary>
/// Raises ProgramWeekReleased once for each week whose calendar release date has arrived.
/// </summary>
internal sealed class WeekReleaseJob(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<WeekReleaseJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Announcing released weeks failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProgramsDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var programs = await db.Programs
            .Include(p => p.Weeks)
            .Where(p => p.Status == ProgramStatus.Published
                && p.Weeks.Any(w => w.ReleasesOn <= today && w.ReleaseAnnouncedAt == null))
            .ToListAsync(cancellationToken);

        var announced = programs.Sum(p => p.AnnounceReleasedWeeks(now));

        if (announced > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Announced {Count} released weeks", announced);
        }

        return announced;
    }
}
