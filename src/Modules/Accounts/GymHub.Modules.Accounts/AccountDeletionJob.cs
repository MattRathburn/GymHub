using GymHub.BuildingBlocks.Identity;
using GymHub.Modules.Accounts.Data;
using GymHub.Modules.Accounts.Features.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymHub.Modules.Accounts;

/// <summary>
/// Deletes accounts whose grace period is over. For each one: re-check the blockers (a blocked deletion waits until
/// they clear), erase every module's data, then delete the Keycloak user, so a failure part-way leaves a user who
/// can still sign in and a row that is retried on the next run. Every step is idempotent.
/// </summary>
internal sealed class AccountDeletionJob(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<AccountDeletionJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Deleting accounts failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Returns how many accounts were deleted.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var userIds = await db.Deletions
            .Where(d => d.CompletedAt == null && d.ScheduledFor <= now)
            .OrderBy(d => d.ScheduledFor)
            .Select(d => d.UserId)
            .ToListAsync(cancellationToken);

        var deleted = 0;

        foreach (var userId in userIds)
        {
            try
            {
                // A fresh scope per account, so one failure doesn't leave tracked state behind for the next.
                using var accountScope = scopeFactory.CreateScope();

                if (await DeleteAsync(accountScope.ServiceProvider, userId, cancellationToken))
                {
                    deleted++;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Deleting account {UserId} failed; it will be retried", userId);
            }
        }

        return deleted;
    }

    /// <summary>
    /// Holds a row lock on the deletion from the re-check to the end. A cancel (which deletes the row) either goes
    /// first, and this run skips the account, or waits and then finds nothing to cancel: never "cancelled" and
    /// deleted at once. The modules' erasures run on their own connections; they're idempotent, so a rollback here
    /// just means the next run repeats them.
    /// </summary>
    private async Task<bool> DeleteAsync(IServiceProvider services, string userId, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AccountsDbContext>();
        var personalData = services.GetRequiredService<PersonalData>();

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var deletion = await db.Deletions
                .FromSql($"""SELECT * FROM accounts.account_deletions WHERE "UserId" = {userId} FOR UPDATE""")
                .SingleOrDefaultAsync(cancellationToken);

            // Cancelled since the list was read, or finished by another run.
            if (deletion is null || !deletion.IsDue(timeProvider.GetUtcNow().UtcDateTime))
            {
                return false;
            }

            var blockers = await personalData.GetBlockersAsync(userId, cancellationToken);

            if (blockers.Count > 0)
            {
                logger.LogInformation("Account {UserId} is due for deletion but blocked: {Blockers}", userId, string.Join(" ", blockers));
                return false;
            }

            foreach (var source in personalData.Sources)
            {
                await source.EraseAsync(userId, cancellationToken);
            }

            await services.GetRequiredService<IIdentityAdmin>().DeleteUserAsync(userId, cancellationToken);

            deletion.Complete(timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            services.GetRequiredService<DeletedAccounts>().Remember(userId, deleted: true);
            logger.LogInformation("Deleted account {UserId}", userId);
            return true;
        });
    }
}
