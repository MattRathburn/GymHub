using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Accounts.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GymHub.Modules.Accounts;

/// <summary>
/// Completed deletions. A deletion is permanent, so a "deleted" answer is cached for good; "not deleted" only briefly,
/// since this runs on every authenticated request.
/// </summary>
internal sealed class DeletedAccounts(AccountsDbContext db, IMemoryCache cache) : IDeletedAccounts
{
    private static readonly TimeSpan NotDeletedFor = TimeSpan.FromMinutes(1);

    public async Task<bool> IsDeletedAsync(string userId, CancellationToken cancellationToken)
    {
        var key = CacheKey(userId);

        if (cache.TryGetValue(key, out bool deleted))
        {
            return deleted;
        }

        deleted = await db.Deletions.AnyAsync(d => d.UserId == userId && d.CompletedAt != null, cancellationToken);
        Remember(userId, deleted);
        return deleted;
    }

    public async Task<IReadOnlySet<string>> FindDeletedAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new HashSet<string>();
        }

        var deleted = await db.Deletions.AsNoTracking()
            .Where(d => userIds.Contains(d.UserId) && d.CompletedAt != null)
            .Select(d => d.UserId)
            .ToListAsync(cancellationToken);

        return deleted.ToHashSet();
    }

    /// <summary>Called when a deletion completes, so this instance refuses the user straight away.</summary>
    public void Remember(string userId, bool deleted)
    {
        if (deleted)
        {
            cache.Set(CacheKey(userId), true);
        }
        else
        {
            cache.Set(CacheKey(userId), false, NotDeletedFor);
        }
    }

    private static string CacheKey(string userId) => $"accounts:deleted:{userId}";
}
