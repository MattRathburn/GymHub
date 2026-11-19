using System.Data.Common;
using static GymHub.MigrationService.DatabaseRoles;

namespace GymHub.MigrationService;

/// <summary>
/// Keycloak's database and login role (published deployments): the role owns only that database, so Keycloak never
/// has the server admin's credentials or rights in gymhubdb. Runs as the server admin (a superuser locally, a
/// non-superuser on Azure) through the app database's connection. Safe to repeat; a changed password parameter is
/// applied on the next run.
/// </summary>
public static class KeycloakDatabase
{
    public static async Task EnsureAsync(DbConnection adminConnection, string name, string password, CancellationToken cancellationToken)
    {
        // Used as both the role and the database name.
        RequireIdentifier(name, "KeycloakDatabase:Name");
        await OpenAsync(adminConnection, cancellationToken);

        await EnsureLoginRoleAsync(adminConnection, name, password, cancellationToken);

        // CREATE DATABASE can't run inside a transaction or a DO block.
        if (await ScalarAsync(adminConnection, "SELECT 1 FROM pg_database WHERE datname = @name", cancellationToken, ("name", name)) is null)
        {
            await ExecuteAsync(adminConnection, $"CREATE DATABASE \"{name}\" OWNER \"{name}\"", cancellationToken);
        }
        else
        {
            await ExecuteAsync(adminConnection, $"ALTER DATABASE \"{name}\" OWNER TO \"{name}\"", cancellationToken);
        }

        // Every role may connect to a new database by default; Keycloak's database is Keycloak's only.
        await ExecuteAsync(adminConnection, $"REVOKE CONNECT ON DATABASE \"{name}\" FROM PUBLIC", cancellationToken);
    }
}
