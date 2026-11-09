using System.Diagnostics;
using GymHub.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GymHub.MigrationService;

/// <summary>
/// Creates the database if needed, applies every module's migrations, then stops. The API waits for this to complete.
/// </summary>
public class MigrationWorker(
    IServiceProvider serviceProvider,
    IEnumerable<ModuleDbContextRegistration> contexts,
    IHostEnvironment hostEnvironment,
    IHostApplicationLifetime hostApplicationLifetime,
    ILogger<MigrationWorker> logger) : BackgroundService
{
    private readonly ActivitySource _activitySource = new(hostEnvironment.ApplicationName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity(hostEnvironment.ApplicationName, ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();

            foreach (var registration in contexts)
            {
                var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);

                await EnsureDatabaseAsync(dbContext, cancellationToken);
                await RunMigrationAsync(dbContext, cancellationToken);
                logger.LogInformation("Migrated {Context}", registration.ContextType.Name);
            }
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task EnsureDatabaseAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var dbCreator = dbContext.GetService<IRelationalDatabaseCreator>();

        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            if (!await dbCreator.ExistsAsync(cancellationToken))
            {
                await dbCreator.CreateAsync(cancellationToken);
            }
        });
    }

    private static async Task RunMigrationAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }
}
