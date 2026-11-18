using System.Security.Cryptography;
using System.Text;
using GymHub.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GymHub.BuildingBlocks.Jobs;

/// <summary>
/// Runs a background job's pass on one instance at a time. A pass that finds the job already running elsewhere is
/// skipped, and the job tries again on its next tick.
/// </summary>
public interface ISingletonJobLock
{
    /// <summary>Runs <paramref name="work"/> if no other instance is running <paramref name="job"/>. Returns whether it ran.</summary>
    Task<bool> TryRunAsync(string job, Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}

/// <summary>
/// A Postgres session advisory lock, held for the pass on a connection of a module DbContext: the same data source
/// (credentials, TLS, pooling) the rest of the app uses. If the connection drops, Postgres releases the lock and
/// another instance may start the job; every job is safe to repeat.
/// </summary>
internal sealed class PostgresJobLock(IServiceScopeFactory scopeFactory, IEnumerable<ModuleDbContextRegistration> contexts) : ISingletonJobLock
{
    private readonly Type _contextType = contexts.FirstOrDefault()?.ContextType
        ?? throw new InvalidOperationException("No module DbContext is registered to take job locks with.");

    public async Task<bool> TryRunAsync(string job, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = (DbContext)scope.ServiceProvider.GetRequiredService(_contextType);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        var key = KeyFor(job);

        try
        {
            await using (var acquire = connection.CreateCommand())
            {
                acquire.CommandText = "SELECT pg_try_advisory_lock(@key)";
                acquire.Parameters.Add(new NpgsqlParameter("key", key));

                if (await acquire.ExecuteScalarAsync(cancellationToken) is not true)
                {
                    return false;
                }
            }

            try
            {
                await work(cancellationToken);
                return true;
            }
            finally
            {
                // Closing the connection would release it too (Npgsql resets pooled connections), but don't rely on that.
                await using var release = connection.CreateCommand();
                release.CommandText = "SELECT pg_advisory_unlock(@key)";
                release.Parameters.Add(new NpgsqlParameter("key", key));
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>A stable 64-bit key for the job name (the first 8 bytes of its SHA-256).</summary>
    internal static long KeyFor(string job) => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"gymhub-job:{job}")), 0);
}
