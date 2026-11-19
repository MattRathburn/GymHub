using System.Data.Common;
using System.Text.RegularExpressions;

namespace GymHub.MigrationService;

/// <summary>
/// Login roles the migration service manages as the server admin. On Azure Database for PostgreSQL that admin isn't a
/// superuser: it has CREATEROLE and CREATEDB, and since Postgres 16 it must be a member of a role before it can hand a
/// database to it. It may also not own databases created by Azure (gymhubdb), so anything an owner must do checks that.
/// </summary>
internal static partial class DatabaseRoles
{
    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex Identifier();

    /// <summary>Role and database names go into SQL as identifiers (they can't be parameters), so only plain ones.</summary>
    public static void RequireIdentifier(string name, string setting)
    {
        if (!Identifier().IsMatch(name))
        {
            throw new InvalidOperationException($"{setting} '{name}' isn't a plain lower-case identifier.");
        }
    }

    /// <summary>
    /// Creates the login role, or sets its password, then makes the admin a member of it (needed to make it a
    /// database owner). The password reaches the DO block through a session setting, never spliced into SQL text.
    /// </summary>
    public static async Task EnsureLoginRoleAsync(DbConnection admin, string name, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"The password for database role '{name}' is empty.");
        }

        await ExecuteAsync(admin, "SELECT set_config('gymhub.role_password', @password, false)", cancellationToken, ("password", password));

        try
        {
            await ExecuteAsync(admin, $$"""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{{name}}') THEN
                        EXECUTE format('ALTER ROLE %I LOGIN PASSWORD %L', '{{name}}', current_setting('gymhub.role_password'));
                    ELSE
                        EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', '{{name}}', current_setting('gymhub.role_password'));
                    END IF;
                END
                $do$
                """, cancellationToken);
        }
        finally
        {
            await ExecuteAsync(admin, "SELECT set_config('gymhub.role_password', '', false)", CancellationToken.None);
        }

        if (!await IsMemberOfAsync(admin, name, cancellationToken))
        {
            await ExecuteAsync(admin, $"GRANT \"{name}\" TO CURRENT_USER", cancellationToken);
        }
    }

    /// <summary>Whether the admin owns the database it's connected to (or is a superuser), so it may change who connects.</summary>
    public static async Task<bool> OwnsCurrentDatabaseAsync(DbConnection admin, CancellationToken cancellationToken) =>
        await ScalarAsync(admin, "SELECT pg_has_role(current_user, datdba, 'MEMBER') FROM pg_database WHERE datname = current_database()", cancellationToken) is true;

    private static async Task<bool> IsMemberOfAsync(DbConnection admin, string role, CancellationToken cancellationToken) =>
        await ScalarAsync(admin, "SELECT pg_has_role(current_user, @role, 'SET')", cancellationToken, ("role", role)) is true;

    public static async Task OpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    public static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
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
