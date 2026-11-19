extern alias migrations;

using migrations::GymHub.MigrationService;
using Npgsql;

namespace GH.FunctionalTests;

/// <summary>
/// The migration service's database roles (published deployments): Keycloak's own database and role, and the API's
/// least-privilege role. Run both as a superuser (local) and as an Azure-like admin that only has CREATEROLE and
/// CREATEDB and doesn't own the app database.
/// </summary>
public class KeycloakDatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewName(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    private string As(string user, string password, string? database = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(factory.PostgresConnectionString) { Username = user, Password = password, Pooling = false };

        if (database is not null)
        {
            builder.Database = database;
        }

        return builder.ConnectionString;
    }

    private static async Task RunAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<string> CurrentUserAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT current_user", connection);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<string> RefusedAsync(Func<Task> action) => (await Assert.ThrowsAsync<PostgresException>(action)).SqlState;

    [Fact]
    public async Task As_a_superuser_Keycloak_and_the_API_each_get_only_their_own_database()
    {
        var keycloak = NewName("kc");
        var app = NewName("app");
        var schema = NewName("mod");
        var appDatabase = new NpgsqlConnectionStringBuilder(factory.PostgresConnectionString).Database!;
        await RunAsync(factory.PostgresConnectionString, $"CREATE SCHEMA \"{schema}\"; CREATE TABLE \"{schema}\".items (id int)");

        await using (var admin = new NpgsqlConnection(factory.PostgresConnectionString))
        {
            await KeycloakDatabase.EnsureAsync(admin, keycloak, "kc-password", Ct);
            await AppDatabaseRole.EnsureAsync(admin, app, "app-password", [schema], Ct);
        }

        Assert.Equal(keycloak, await CurrentUserAsync(As(keycloak, "kc-password", keycloak)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await RefusedAsync(() => CurrentUserAsync(As(keycloak, "kc-password", appDatabase))));

        await RunAsync(As(app, "app-password", appDatabase), $"INSERT INTO \"{schema}\".items VALUES (1); SELECT * FROM \"{schema}\".items");
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await RefusedAsync(() => RunAsync(As(app, "app-password", appDatabase), $"CREATE TABLE \"{schema}\".other (id int)")));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await RefusedAsync(() => RunAsync(As(app, "app-password", appDatabase), $"DROP TABLE \"{schema}\".items")));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await RefusedAsync(() => CurrentUserAsync(As(app, "app-password", keycloak))));

        // Running again applies changed passwords and keeps everything.
        await using (var admin = new NpgsqlConnection(factory.PostgresConnectionString))
        {
            await KeycloakDatabase.EnsureAsync(admin, keycloak, "kc-password-2", Ct);
            await AppDatabaseRole.EnsureAsync(admin, app, "app-password-2", [schema], Ct);
        }

        Assert.Equal(keycloak, await CurrentUserAsync(As(keycloak, "kc-password-2", keycloak)));
        Assert.Equal(app, await CurrentUserAsync(As(app, "app-password-2", appDatabase)));
        await RefusedAsync(() => CurrentUserAsync(As(keycloak, "kc-password", keycloak)));
    }

    [Fact]
    public async Task Works_for_an_Azure_like_admin_that_is_not_a_superuser_and_does_not_own_the_app_database()
    {
        // Azure Database for PostgreSQL: the admin has CREATEROLE and CREATEDB, and gymhubdb was created by Azure.
        var admin = NewName("azadmin");
        var armDatabase = NewName("armdb");
        var keycloak = NewName("kc");
        var app = NewName("app");
        await RunAsync(factory.PostgresConnectionString, $"CREATE ROLE \"{admin}\" LOGIN PASSWORD 'admin-password' CREATEROLE CREATEDB");
        await RunAsync(factory.PostgresConnectionString, $"CREATE DATABASE \"{armDatabase}\"");
        await RunAsync(factory.PostgresConnectionString, $"GRANT CREATE ON DATABASE \"{armDatabase}\" TO \"{admin}\"");

        // The migrations create the module schemas as the admin.
        await RunAsync(As(admin, "admin-password", armDatabase), "CREATE SCHEMA mod; CREATE TABLE mod.items (id int)");

        await using (var connection = new NpgsqlConnection(As(admin, "admin-password", armDatabase)))
        {
            await KeycloakDatabase.EnsureAsync(connection, keycloak, "kc-password", Ct);
            await AppDatabaseRole.EnsureAsync(connection, app, "app-password", ["mod"], Ct);
        }

        Assert.Equal(keycloak, await CurrentUserAsync(As(keycloak, "kc-password", keycloak)));
        await RunAsync(As(app, "app-password", armDatabase), "INSERT INTO mod.items VALUES (1); SELECT * FROM mod.items");
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await RefusedAsync(() => RunAsync(As(app, "app-password", armDatabase), "CREATE TABLE mod.other (id int)")));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await RefusedAsync(() => CurrentUserAsync(As(app, "app-password", keycloak))));

        // The admin still gets into the app database it doesn't own (nothing revoked its access), and can run it again.
        Assert.Equal(admin, await CurrentUserAsync(As(admin, "admin-password", armDatabase)));
        await using (var connection = new NpgsqlConnection(As(admin, "admin-password", armDatabase)))
        {
            await KeycloakDatabase.EnsureAsync(connection, keycloak, "kc-password", Ct);
            await AppDatabaseRole.EnsureAsync(connection, app, "app-password", ["mod"], Ct);
        }
    }

    [Theory]
    [InlineData("Keycloak")]
    [InlineData("keycloak; DROP DATABASE x")]
    [InlineData("key\"cloak")]
    public async Task Names_that_are_not_plain_identifiers_are_refused(string name)
    {
        await using var admin = new NpgsqlConnection(factory.PostgresConnectionString);
        await Assert.ThrowsAsync<InvalidOperationException>(() => KeycloakDatabase.EnsureAsync(admin, name, "password", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AppDatabaseRole.EnsureAsync(admin, name, "password", ["mod"], Ct));
    }
}
