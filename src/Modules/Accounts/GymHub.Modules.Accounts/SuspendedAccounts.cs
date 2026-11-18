using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Accounts.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GymHub.Modules.Accounts;

/// <summary>
/// Suspensions, checked on every authenticated request. Answers are cached briefly; the instance that suspends or
/// reactivates someone updates its cache straight away, others catch up within <see cref="CacheFor"/>.
/// </summary>
internal sealed class SuspendedAccounts(AccountsDbContext db, IMemoryCache cache) : ISuspendedAccounts
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    public async Task<bool> IsSuspendedAsync(string userId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey(userId), out bool suspended))
        {
            return suspended;
        }

        suspended = await db.Suspensions.AnyAsync(s => s.UserId == userId, cancellationToken);
        Remember(userId, suspended);
        return suspended;
    }

    public void Remember(string userId, bool suspended) => cache.Set(CacheKey(userId), suspended, CacheFor);

    private static string CacheKey(string userId) => $"accounts:suspended:{userId}";
}
