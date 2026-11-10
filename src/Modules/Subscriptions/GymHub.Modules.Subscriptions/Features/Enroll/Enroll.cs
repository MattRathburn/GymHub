using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Subscriptions.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Subscriptions.Features.Enroll;

/// <summary>
/// Enrolling and leaving. Only free programs can be joined here; paid ones go through checkout (Billing plan).
/// </summary>
internal sealed class EnrollHandler(SubscriptionsDbContext db, IProgramCatalog catalog, TimeProvider timeProvider)
{
    public async Task<Result> Enroll(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var program = await catalog.GetSummaryAsync(programId, cancellationToken);

        if (program is null || program.Status == ProgramStatus.Draft)
        {
            return Error.NotFound("Program not found.");
        }

        if (program.Status == ProgramStatus.Archived)
        {
            return Error.Conflict("This program is no longer open to new subscribers.");
        }

        if (!program.Price.IsFree)
        {
            return Error.PaymentRequired("This is a paid program. Checkout is required to subscribe.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await db.Enrollments.SingleOrDefaultAsync(e => e.UserId == user.Id && e.ProgramId == programId, cancellationToken);

        if (existing is { IsActive: true })
        {
            return Result.Success();
        }

        if (existing is null)
        {
            db.Enrollments.Add(Enrollment.StartFree(user.Id, programId, now));
        }
        else
        {
            existing.Reactivate(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Leave(Guid programId, CurrentUser user, CancellationToken cancellationToken)
    {
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.UserId == user.Id && e.ProgramId == programId, cancellationToken);

        if (enrollment is not { IsActive: true })
        {
            return Error.NotFound("You're not subscribed to this program.");
        }

        enrollment.Cancel(timeProvider.GetUtcNow().UtcDateTime);
        db.AddIntegrationEvent(new EnrollmentCancelled(user.Id, programId));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class EnrollEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/programs/{programId:guid}", async (Guid programId, ClaimsPrincipal user, EnrollHandler handler, CancellationToken ct) =>
                (await handler.Enroll(programId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("Enroll");

        app.MapDelete("/programs/{programId:guid}", async (Guid programId, ClaimsPrincipal user, EnrollHandler handler, CancellationToken ct) =>
                (await handler.Leave(programId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("LeaveProgram");
    }
}
