using GymHub.BuildingBlocks.Admin;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Identity;
using GymHub.Modules.Accounts.Data;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Accounts;

/// <summary>Users and trainers (from Keycloak), suspensions and deletions waiting out their grace period.</summary>
internal sealed class AccountsMetrics(AccountsDbContext db, IIdentityAdmin identity) : IAdminMetricsSource
{
    public async Task<IReadOnlyList<AdminMetric>> GetMetricsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var users = await identity.CountUsersAsync(null, cancellationToken);
        var trainers = (await identity.GetRoleMemberIdsAsync(Roles.Trainer, cancellationToken)).Count;
        var suspended = await db.Suspensions.CountAsync(cancellationToken);
        var deleting = await db.Deletions.CountAsync(d => d.CompletedAt == null, cancellationToken);

        return
        [
            new(AdminSections.Users, "Users", users, Link: "/admin/users"),
            new(AdminSections.Users, "Trainers", trainers),
            new(AdminSections.Users, "Suspended", suspended),
            new(AdminSections.Users, "Deletions scheduled", deleting)
        ];
    }
}
