using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Accounts.Data;
using GymHub.Modules.Accounts.Domain;
using GymHub.Modules.Accounts.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Accounts.Features.ScheduleDeletion;

/// <summary>The member types <see cref="ScheduleDeletionValidator.Phrase"/> to confirm.</summary>
public sealed record ScheduleDeletionRequest(string Confirmation);

internal sealed class ScheduleDeletionValidator : AbstractValidator<ScheduleDeletionRequest>
{
    public const string Phrase = "delete my account";

    public ScheduleDeletionValidator()
    {
        RuleFor(r => r.Confirmation)
            .Must(c => string.Equals(c?.Trim(), Phrase, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"Type \"{Phrase}\" to confirm.");
    }
}

/// <summary>
/// Schedules the deletion after the grace period, or cancels it. Blockers (a renewing subscription, published
/// programs) stop the request; <see cref="AccountDeletionJob"/> checks them again before erasing anything.
/// </summary>
internal sealed class ScheduleDeletionHandler(AccountsDbContext db, PersonalData personalData, TimeProvider timeProvider)
{
    public async Task<Result<DateTime>> Schedule(CurrentUser user, CancellationToken cancellationToken)
    {
        var existing = await db.Deletions.SingleOrDefaultAsync(d => d.UserId == user.Id, cancellationToken);

        if (existing is { IsCompleted: true })
        {
            return Error.Conflict("This account has been deleted.");
        }

        if (existing is not null)
        {
            return existing.ScheduledFor;
        }

        var blockers = await personalData.GetBlockersAsync(user.Id, cancellationToken);

        if (blockers.Count > 0)
        {
            return Error.Conflict(string.Join(" ", blockers));
        }

        var deletion = AccountDeletion.Schedule(user.Id, timeProvider.GetUtcNow().UtcDateTime);
        db.Deletions.Add(deletion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return deletion.ScheduledFor;
        }
        catch (DbUpdateException)
        {
            // A second request (a double click, another tab) got there first: answer with its date.
            if (await ScheduledElsewhereAsync(user.Id, cancellationToken) is { } scheduledFor)
            {
                return scheduledFor;
            }

            throw;
        }
    }

    private async Task<DateTime?> ScheduledElsewhereAsync(string userId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return await db.Deletions.AsNoTracking()
            .Where(d => d.UserId == userId && d.CompletedAt == null)
            .Select(d => (DateTime?)d.ScheduledFor)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Result> Cancel(CurrentUser user, CancellationToken cancellationToken)
    {
        var removed = await db.Deletions
            .Where(d => d.UserId == user.Id && d.CompletedAt == null)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0 ? Error.NotFound("No account deletion is scheduled.") : Result.Success();
    }
}

internal static class ScheduleDeletionEndpoint
{
    public sealed record ScheduledDto(DateTime ScheduledFor);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/deletion", async (ScheduleDeletionRequest request, ClaimsPrincipal user, ScheduleDeletionHandler handler, CancellationToken ct) =>
                (await handler.Schedule(CurrentUser.Required(user), ct)).ToHttp(scheduledFor => TypedResults.Ok(new ScheduledDto(scheduledFor))))
            .RequireAuthorization()
            .WithValidation<ScheduleDeletionRequest>()
            .WithName("ScheduleAccountDeletion");

        app.MapDelete("/deletion", async (ClaimsPrincipal user, ScheduleDeletionHandler handler, CancellationToken ct) =>
                (await handler.Cancel(CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("CancelAccountDeletion");
    }
}
