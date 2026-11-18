using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Identity;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Trainers.Contracts;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Trainers.Features.RevokeTrainer;

/// <summary>
/// Admin: takes the trainer role away. Records the revocation (Programs refuses their publishes from now on, even
/// with a token that still has the role), marks their approved application revoked so they may apply again, and
/// raises <see cref="TrainerRevoked"/>: the role removal and the archiving of their programs run from the outbox.
/// The profile stays: archived programs still show the trainer's name.
/// </summary>
internal sealed class RevokeTrainerHandler(TrainersDbContext db, IIdentityAdmin identity, TimeProvider timeProvider)
{
    public async Task<Result> Handle(string userId, CurrentUser admin, CancellationToken cancellationToken)
    {
        var approved = await db.Applications
            .Where(a => a.UserId == userId && a.Status == ApplicationStatus.Approved)
            .ToListAsync(cancellationToken);

        var direct = (await identity.GetRealmRolesAsync([userId], effective: false, cancellationToken)).GetValueOrDefault(userId) ?? new HashSet<string>();
        var effective = (await identity.GetRealmRolesAsync([userId], effective: true, cancellationToken)).GetValueOrDefault(userId) ?? new HashSet<string>();

        // GymHub only removes the role mapped to the user; one from a group or composite role would stay.
        if (effective.Contains(Roles.Trainer) && !direct.Contains(Roles.Trainer))
        {
            return Error.Conflict("This user gets the trainer role from a Keycloak group or composite role. Remove it there.");
        }

        // Trainers seeded in Keycloak have the role without an application.
        if (approved.Count == 0 && !direct.Contains(Roles.Trainer))
        {
            return Error.NotFound("This user isn't a trainer.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var application in approved)
        {
            application.Revoke(admin.Id, now);
        }

        if (!await db.Revocations.AnyAsync(r => r.UserId == userId, cancellationToken))
        {
            db.Revocations.Add(new TrainerRevocation(userId, admin.Id, now));
        }

        db.AddIntegrationEvent(new TrainerRevoked(userId));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class RevokeTrainerEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{userId}/revoke", async (string userId, ClaimsPrincipal user, RevokeTrainerHandler handler, CancellationToken ct) =>
                (await handler.Handle(userId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization(Policies.Admin)
            .WithName("RevokeTrainer");
}
