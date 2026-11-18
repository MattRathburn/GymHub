using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Identity;
using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Accounts.Data;
using GymHub.Modules.Accounts.Domain;
using GymHub.Modules.Trainers.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Accounts.Features.AdminUsers;

public sealed record SuspensionDto(DateTime SuspendedAt, string? Reason);

/// <summary>A user for the admin's user list. <c>Enabled</c> is Keycloak's flag, which a suspension turns off.</summary>
public sealed record AdminUserDto(
    string Id,
    string Username,
    string? Email,
    string? DisplayName,
    bool EmailVerified,
    bool Enabled,
    DateTime? CreatedAt,
    bool IsTrainer,
    bool IsAdmin,
    SuspensionDto? Suspension,
    DateTime? DeletionScheduledFor);

public sealed record SuspendUserRequest(string? Reason);

internal sealed class SuspendUserValidator : AbstractValidator<SuspendUserRequest>
{
    public SuspendUserValidator() => RuleFor(r => r.Reason).MaximumLength(AccountSuspension.MaxReasonLength);
}

/// <summary>
/// Users come from Keycloak (it holds every account, including those who never opened a page that stores
/// anything), with roles, suspensions, pending deletions and profile names added.
/// </summary>
internal sealed class AdminUsersHandler(
    AccountsDbContext db, IIdentityAdmin identity, IProfileDirectory profiles, SuspendedAccounts suspended, TimeProvider timeProvider)
{
    public async Task<PagedResult<AdminUserDto>> List(string? search, PaginationRequest page, CancellationToken cancellationToken)
    {
        var counting = identity.CountUsersAsync(search, cancellationToken);
        var users = await identity.SearchUsersAsync(search, page.Skip, page.Take, cancellationToken);
        var count = await counting;

        if (users.Count == 0)
        {
            return new PagedResult<AdminUserDto>(page.PageIndex, page.Take, count, []);
        }

        var ids = users.Select(u => u.Id).ToList();

        // Effective roles of this page's users only: what their tokens carry, including roles from groups.
        var roles = await identity.GetRealmRolesAsync(ids, effective: true, cancellationToken);
        bool Has(string userId, string role) => roles.GetValueOrDefault(userId)?.Contains(role) == true;
        var names = await profiles.GetAuthorsAsync(ids, cancellationToken);

        var suspensions = await db.Suspensions.AsNoTracking()
            .Where(s => ids.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, cancellationToken);

        var deletions = await db.Deletions.AsNoTracking()
            .Where(d => ids.Contains(d.UserId) && d.CompletedAt == null)
            .ToDictionaryAsync(d => d.UserId, d => d.ScheduledFor, cancellationToken);

        return new PagedResult<AdminUserDto>(page.PageIndex, page.Take, count, [.. users.Select(u => new AdminUserDto(
            u.Id,
            u.Username,
            u.Email,
            names.GetValueOrDefault(u.Id)?.DisplayName,
            u.EmailVerified,
            u.Enabled,
            u.CreatedAt,
            Has(u.Id, Roles.Trainer),
            Has(u.Id, Roles.Admin),
            suspensions.GetValueOrDefault(u.Id) is { } s ? new SuspensionDto(s.SuspendedAt, s.Reason) : null,
            deletions.TryGetValue(u.Id, out var scheduledFor) ? scheduledFor : null))]);
    }

    /// <summary>
    /// Saves the suspension first, so the API refuses the user from then on and Reactivate can always undo it, then
    /// disables the Keycloak user (ending their sessions). If Keycloak fails, suspending again finishes the job.
    /// Suspending again, or two admins at once, is harmless.
    /// </summary>
    public async Task<Result> Suspend(CurrentUser admin, string userId, SuspendUserRequest request, CancellationToken cancellationToken)
    {
        if (userId == admin.Id)
        {
            return Error.Conflict("You can't suspend yourself.");
        }

        // Effective roles, so an admin who gets the role from a Keycloak group is protected too.
        var roles = await identity.GetRealmRolesAsync([userId], effective: true, cancellationToken);

        if (roles.GetValueOrDefault(userId)?.Contains(Roles.Admin) == true)
        {
            return Error.Conflict("Admins can't be suspended. Remove their admin role in Keycloak first.");
        }

        var added = await AddSuspensionAsync(admin, userId, request, cancellationToken);
        suspended.Remember(userId, true);

        if (!await identity.SetEnabledAsync(userId, false, cancellationToken))
        {
            if (added)
            {
                await db.Suspensions.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                suspended.Remember(userId, false);
            }

            return Error.NotFound("User not found.");
        }

        return Result.Success();
    }

    /// <summary>True when this request added the row; false when it was there already (or another request added it).</summary>
    private async Task<bool> AddSuspensionAsync(CurrentUser admin, string userId, SuspendUserRequest request, CancellationToken cancellationToken)
    {
        if (await db.Suspensions.AnyAsync(s => s.UserId == userId, cancellationToken))
        {
            return false;
        }

        db.Suspensions.Add(new AccountSuspension(userId, admin.Id, request.Reason, timeProvider.GetUtcNow().UtcDateTime));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();

            if (await db.Suspensions.AnyAsync(s => s.UserId == userId, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    public async Task<Result> Reactivate(string userId, CancellationToken cancellationToken)
    {
        var suspension = await db.Suspensions.SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (suspension is null)
        {
            return Error.NotFound("This user isn't suspended.");
        }

        // A user deleted in Keycloak meanwhile has nothing to enable; the row goes either way.
        await identity.SetEnabledAsync(userId, true, cancellationToken);

        db.Suspensions.Remove(suspension);
        await db.SaveChangesAsync(cancellationToken);
        suspended.Remember(userId, false);
        return Result.Success();
    }
}

internal static class AdminUsersEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/admin/users").RequireAuthorization(Policies.Admin);

        users.MapGet("/", async (string? search, [AsParameters] PaginationRequest page, AdminUsersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.List(search, page, ct)))
            .WithName("AdminListUsers");

        users.MapPost("/{userId}/suspension", async (string userId, SuspendUserRequest request, ClaimsPrincipal user, AdminUsersHandler handler, CancellationToken ct) =>
                (await handler.Suspend(CurrentUser.Required(user), userId, request, ct)).ToHttp())
            .WithValidation<SuspendUserRequest>()
            .WithName("AdminSuspendUser");

        users.MapDelete("/{userId}/suspension", async (string userId, AdminUsersHandler handler, CancellationToken ct) =>
                (await handler.Reactivate(userId, ct)).ToHttp())
            .WithName("AdminReactivateUser");
    }
}
