using GymHub.BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        builder.AddNpgsqlDbContext<TContext>(ConnectionName, configureDbContextOptions: options =>
        {
            options.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema));
        });

        builder.Services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext)));

        return builder;
    }
}

/// <summary>
/// Marks a DbContext type as a module context, so the outbox dispatcher and migration service can find every one.
/// </summary>
public sealed record ModuleDbContextRegistration(Type ContextType);
