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
    IConfiguration configuration,
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

            // Published deployments: Keycloak's own database and role (the AppHost sets these, and Keycloak waits).
            if (configuration["KeycloakDatabase:Password"] is { Length: > 0 } keycloakPassword)
            {
                var admin = (DbContext)scope.ServiceProvider.GetRequiredService(contexts.First().ContextType);
                var name = configuration["KeycloakDatabase:Name"] ?? "keycloak";
                await KeycloakDatabase.EnsureAsync(admin.Database.GetDbConnection(), name, keycloakPassword, cancellationToken);
                logger.LogInformation("Keycloak database {Name} and its role are in place", name);
            }

            // Published deployments: the API's own least-privilege role, granted every module schema after migrating.
            if (configuration["AppDatabase:Password"] is { Length: > 0 } appPassword)
            {
                var admin = (DbContext)scope.ServiceProvider.GetRequiredService(contexts.First().ContextType);
                var name = configuration["AppDatabase:User"] ?? "gymhub_app";
                var schemas = contexts.Select(c => ((ModuleDbContext)scope.ServiceProvider.GetRequiredService(c.ContextType)).Schema);
                await AppDatabaseRole.EnsureAsync(admin.Database.GetDbConnection(), name, appPassword, schemas, cancellationToken);
                logger.LogInformation("Database role {Name} can use the module schemas", name);
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
