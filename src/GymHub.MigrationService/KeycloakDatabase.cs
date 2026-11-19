using System.Data.Common;
using System.Text.RegularExpressions;

namespace GymHub.MigrationService;

/// <summary>
/// Keycloak's database and login role (published deployments): the role owns only that database, and the app
/// database no longer lets every role connect, so Keycloak never has the server admin's credentials or a way into
/// gymhubdb. Runs as the server admin through the app database's connection. Safe to repeat; a changed password
/// parameter is applied on the next run.
/// </summary>
public static partial class KeycloakDatabase
{
    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex Identifier();

    public static async Task EnsureAsync(DbConnection adminConnection, string name, string password, CancellationToken cancellationToken)
    {
        // Used as both the role and the database name, and identifiers can't be query parameters.
        if (!Identifier().IsMatch(name))
        {
            throw new InvalidOperationException($"KeycloakDatabase:Name '{name}' isn't a plain lower-case identifier.");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("KeycloakDatabase:Password is empty.");
        }

        if (adminConnection.State != System.Data.ConnectionState.Open)
        {
            await adminConnection.OpenAsync(cancellationToken);
        }

        // The password reaches the DO block through a session setting, so it is never spliced into SQL text.
        await ExecuteAsync(adminConnection, "SELECT set_config('gymhub.keycloak_password', @password, false)", cancellationToken, ("password", password));
        await ExecuteAsync(adminConnection, $$"""
            DO $do$
            BEGIN
                IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{{name}}') THEN
                    EXECUTE format('ALTER ROLE %I LOGIN PASSWORD %L', '{{name}}', current_setting('gymhub.keycloak_password'));
                ELSE
                    EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', '{{name}}', current_setting('gymhub.keycloak_password'));
                END IF;
            END
            $do$
            """, cancellationToken);
        await ExecuteAsync(adminConnection, "SELECT set_config('gymhub.keycloak_password', '', false)", cancellationToken);

        // CREATE DATABASE can't run inside a transaction or a DO block.
        if (await ScalarAsync(adminConnection, "SELECT 1 FROM pg_database WHERE datname = @name", cancellationToken, ("name", name)) is null)
        {
            await ExecuteAsync(adminConnection, $"CREATE DATABASE \"{name}\" OWNER \"{name}\"", cancellationToken);
        }
        else
        {
            await ExecuteAsync(adminConnection, $"ALTER DATABASE \"{name}\" OWNER TO \"{name}\"", cancellationToken);
        }

        // Every role may connect to a new database by default; the app database is the API's and the migrations' only.
        await ExecuteAsync(adminConnection, """
            DO $do$
            BEGIN
                EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database());
            END
            $do$
            """, cancellationToken);
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static DbCommand Command(DbConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (parameterName, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
