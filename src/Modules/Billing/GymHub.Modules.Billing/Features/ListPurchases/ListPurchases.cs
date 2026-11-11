using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Paging;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Payments;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Trainers.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Billing.Features.ListPurchases;

public sealed record PurchaseDto(
    Guid Id,
    Guid ProgramId,
    string ProgramTitle,
    string UserId,
    string? BuyerName,
    PurchaseKind Kind,
    PurchaseStatus Status,
    long AmountMinor,
    string Currency,
    BillingInterval? Interval,
    long FeeMinor,
    DateTime CreatedAt,
    DateTime? LastPaymentAt,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTime? TrialEndsAt,
    long? NextAmountMinor,
    BillingInterval? NextInterval,
    bool CanRefund);

public sealed record PurchaseFilter(PurchaseStatus? Status, Guid? ProgramId, int PageIndex = 0, int PageSize = 20);

/// <summary>Purchases for the buyer (their own), the trainer (their sales) and admins (all).</summary>
internal sealed class ListPurchasesHandler(
    BillingDbContext db, IProgramCatalog catalog, IProfileDirectory profiles, IOptions<BillingOptions> options, TimeProvider timeProvider)
{
    /// <summary>The member's purchases that still matter: those giving access, plus the latest of each program.</summary>
    public async Task<IReadOnlyList<PurchaseDto>> Mine(CurrentUser user, CancellationToken cancellationToken)
    {
        var purchases = await db.Purchases.AsNoTracking()
            .Where(p => p.UserId == user.Id && p.Status != PurchaseStatus.Pending)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var latest = purchases.GroupBy(p => p.ProgramId).Select(g => g.FirstOrDefault(p => p.GivesAccess) ?? g.First()).ToList();
        return await ToDtosAsync(latest, user, cancellationToken);
    }

    public Task<PagedResult<PurchaseDto>> Sales(CurrentUser user, PurchaseFilter filter, CancellationToken cancellationToken) =>
        PageAsync(db.Purchases.Where(p => p.TrainerId == user.Id), user, filter, cancellationToken);

    public Task<PagedResult<PurchaseDto>> All(CurrentUser user, PurchaseFilter filter, CancellationToken cancellationToken) =>
        PageAsync(db.Purchases, user, filter, cancellationToken);

    private async Task<PagedResult<PurchaseDto>> PageAsync(
        IQueryable<Purchase> query, CurrentUser user, PurchaseFilter filter, CancellationToken cancellationToken)
    {
        var paging = new PaginationRequest(filter.PageSize, filter.PageIndex);
        query = query.AsNoTracking().Where(p => p.Status != PurchaseStatus.Pending);

        if (filter.Status is { } status)
        {
            query = query.Where(p => p.Status == status);
        }

        if (filter.ProgramId is { } programId)
        {
            query = query.Where(p => p.ProgramId == programId);
        }

        var count = await query.LongCountAsync(cancellationToken);
        var page = await query.OrderByDescending(p => p.CreatedAt).Skip(paging.Skip).Take(paging.Take).ToListAsync(cancellationToken);

        return new PagedResult<PurchaseDto>(paging.PageIndex, paging.Take, count, await ToDtosAsync(page, user, cancellationToken));
    }

    private async Task<IReadOnlyList<PurchaseDto>> ToDtosAsync(IReadOnlyList<Purchase> purchases, CurrentUser user, CancellationToken cancellationToken)
    {
        if (purchases.Count == 0)
        {
            return [];
        }

        var programs = await catalog.GetSummariesAsync([.. purchases.Select(p => p.ProgramId).Distinct()], cancellationToken);
        var buyers = await profiles.GetAuthorsAsync([.. purchases.Select(p => p.UserId).Distinct()], cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return [.. purchases.Select(p => new PurchaseDto(
            p.Id,
            p.ProgramId,
            programs.GetValueOrDefault(p.ProgramId)?.Title ?? "Removed program",
            p.UserId,
            buyers.GetValueOrDefault(p.UserId)?.DisplayName,
            p.Kind,
            p.Status,
            p.AmountMinor,
            p.Currency,
            p.Interval,
            p.FeeMinor,
            p.CreatedAt,
            p.LastPaymentAt,
            p.CurrentPeriodEnd,
            p.CancelAtPeriodEnd,
            p.TrialEndsAt,
            p.ScheduledAmountMinor,
            p.ScheduledInterval,
            p.CanBeRefundedBy(user, options.Value.TrainerRefundWindowDays, now).IsSuccess))];
    }
}

internal static class ListPurchasesEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/me/purchases", async (ClaimsPrincipal user, ListPurchasesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Mine(CurrentUser.Required(user), ct)))
            .RequireAuthorization()
            .WithName("ListMyPurchases");

        app.MapGet("/sales", async ([AsParameters] PurchaseFilter filter, ClaimsPrincipal user, ListPurchasesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Sales(CurrentUser.Required(user), filter, ct)))
            .RequireAuthorization(Policies.Trainer)
            .WithName("ListMySales");

        app.MapGet("/purchases", async ([AsParameters] PurchaseFilter filter, ClaimsPrincipal user, ListPurchasesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.All(CurrentUser.Required(user), filter, ct)))
            .RequireAuthorization(Policies.Admin)
            .WithName("ListPurchases");
    }
}
