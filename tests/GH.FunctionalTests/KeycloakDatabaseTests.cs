extern alias migrations;

using migrations::GymHub.MigrationService;
using Npgsql;

namespace GH.FunctionalTests;

/// <summary>The migration service's setup of Keycloak's own database and role (published deployments).</summary>
public class KeycloakDatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string As(string user, string password, string? database = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(factory.PostgresConnectionString) { Username = user, Password = password, Pooling = false };

        if (database is not null)
        {
            builder.Database = database;
        }

        return builder.ConnectionString;
    }

    private async Task<string> CurrentUserAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT current_user", connection);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task Keycloak_gets_its_own_database_and_role_and_no_way_into_the_app_database()
    {
        var name = $"keycloak_{Guid.NewGuid():N}"[..20];
        var appDatabase = new NpgsqlConnectionStringBuilder(factory.PostgresConnectionString).Database!;

        await using (var admin = new NpgsqlConnection(factory.PostgresConnectionString))
        {
            await KeycloakDatabase.EnsureAsync(admin, name, "first-password", Ct);
        }

        Assert.Equal(name, await CurrentUserAsync(As(name, "first-password", name)));

        var refused = await Assert.ThrowsAsync<PostgresException>(() => CurrentUserAsync(As(name, "first-password", appDatabase)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);

        // Running again applies a changed password and keeps the database.
        await using (var admin = new NpgsqlConnection(factory.PostgresConnectionString))
        {
            await KeycloakDatabase.EnsureAsync(admin, name, "second-password", Ct);
        }

        Assert.Equal(name, await CurrentUserAsync(As(name, "second-password", name)));
        await Assert.ThrowsAsync<PostgresException>(() => CurrentUserAsync(As(name, "first-password", name)));
    }

    [Theory]
    [InlineData("Keycloak")]
    [InlineData("keycloak; DROP DATABASE x")]
    [InlineData("key\"cloak")]
    public async Task Names_that_are_not_plain_identifiers_are_refused(string name)
    {
        await using var admin = new NpgsqlConnection(factory.PostgresConnectionString);
        await Assert.ThrowsAsync<InvalidOperationException>(() => KeycloakDatabase.EnsureAsync(admin, name, "password", Ct));
    }
}
