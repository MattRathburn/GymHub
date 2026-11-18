using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Accounts.Data;
using GymHub.Modules.Accounts.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Accounts.Features.AccountStatus;

public sealed record PendingDeletionDto(DateTime RequestedAt, DateTime ScheduledFor);

/// <summary>Blockers are what stops the deletion now; a pending deletion waits for them to clear.</summary>
public sealed record AccountStatusDto(PendingDeletionDto? Deletion, IReadOnlyList<string> DeletionBlockers);

internal sealed class AccountStatusHandler(AccountsDbContext db, PersonalData personalData)
{
    public async Task<AccountStatusDto> Handle(CurrentUser user, CancellationToken cancellationToken)
    {
        var deletion = await db.Deletions.AsNoTracking()
            .Where(d => d.UserId == user.Id && d.CompletedAt == null)
            .Select(d => new PendingDeletionDto(d.RequestedAt, d.ScheduledFor))
            .SingleOrDefaultAsync(cancellationToken);

        return new AccountStatusDto(deletion, await personalData.GetBlockersAsync(user.Id, cancellationToken));
    }
}

internal static class AccountStatusEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (ClaimsPrincipal user, AccountStatusHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(CurrentUser.Required(user), ct)))
            .RequireAuthorization()
            .WithName("GetAccountStatus");
}
