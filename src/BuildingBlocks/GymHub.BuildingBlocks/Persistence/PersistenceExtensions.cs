using GymHub.BuildingBlocks.Jobs;
using GymHub.BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace GymHub.BuildingBlocks.Persistence;

public static class PersistenceExtensions
{
    /// <summary>Aspire connection name of the single GymHub database.</summary>
    public const string ConnectionName = "gymhubdb";

    /// <summary>
    /// Registers a module DbContext against the shared database, keeping its migrations history in its own schema,
    /// and enrolls it in outbox dispatch and in the migration service.
    /// </summary>
    public static IHostApplicationBuilder AddModuleDbContext<TContext>(this IHostApplicationBuilder builder, string schema)
        where TContext : ModuleDbContext
    {
        ApplyDatabasePassword(builder.Configuration);

        builder.AddNpgsqlDbContext<TContext>(ConnectionName, configureDbContextOptions: options =>
        {
            options.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema));
        });

        builder.Services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext)));
        builder.Services.TryAddSingleton<ISingletonJobLock, PostgresJobLock>();

        return builder;
    }

    /// <summary>
    /// Published deployments give the API a connection string for its own role without the password, and the password
    /// (a Key Vault secret) on its own as Database:Password; this puts them together before anything reads it.
    /// </summary>
    private static void ApplyDatabasePassword(IConfigurationManager configuration)
    {
        var key = $"ConnectionStrings:{ConnectionName}";

        if (configuration["Database:Password"] is { Length: > 0 } password && configuration[key] is { Length: > 0 } connectionString)
        {
            configuration[key] = new NpgsqlConnectionStringBuilder(connectionString) { Password = password }.ConnectionString;
        }
    }
}

/// <summary>
/// Marks a DbContext type as a module context, so the outbox dispatcher and migration service can find every one.
/// </summary>
public sealed record ModuleDbContextRegistration(Type ContextType);
