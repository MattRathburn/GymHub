using System.Data.Common;
using static GymHub.MigrationService.DatabaseRoles;

namespace GymHub.MigrationService;

/// <summary>
/// The API's own login role (published deployments): it reads and writes the modules' tables and nothing else. No
/// DDL, no other databases, no role management; only the migration service keeps the server admin's credentials.
/// Re-run after every migration (the job does), so new tables are covered. Safe to repeat.
/// </summary>
public static class AppDatabaseRole
{
    public static async Task EnsureAsync(
        DbConnection adminConnection, string name, string password, IEnumerable<string> schemas, CancellationToken cancellationToken)
    {
        RequireIdentifier(name, "AppDatabase:User");
        var schemaList = schemas.Distinct().ToList();
        schemaList.ForEach(schema => RequireIdentifier(schema, "module schema"));

        await OpenAsync(adminConnection, cancellationToken);
        await EnsureLoginRoleAsync(adminConnection, name, password, cancellationToken);

        // Only the owner can change who may connect. On Azure the admin may not own gymhubdb (Azure created it); then
        // PUBLIC keeps CONNECT, which gives nothing without the schema grants below.
        if (await OwnsCurrentDatabaseAsync(adminConnection, cancellationToken))
        {
            await ExecuteAsync(adminConnection, """
                DO $do$
                BEGIN
                    EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database());
                END
                $do$
                """, cancellationToken);
            await ExecuteAsync(adminConnection, $$"""
                DO $do$
                BEGIN
                    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), '{{name}}');
                END
                $do$
                """, cancellationToken);
        }

        foreach (var schema in schemaList)
        {
            await ExecuteAsync(adminConnection, $"""
                GRANT USAGE ON SCHEMA "{schema}" TO "{name}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "{schema}" TO "{name}";
                GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA "{schema}" TO "{name}";
                """, cancellationToken);
        }
    }
}
