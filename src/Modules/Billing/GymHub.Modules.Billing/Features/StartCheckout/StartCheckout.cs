using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Features.Shared;
using GymHub.Modules.Billing.Payments;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Billing.Features.StartCheckout;

/// <summary>
/// Starts Stripe Checkout for a paid program. The member is redirected to Stripe; access is granted by the
/// checkout.session.completed webhook, not by the return to the app.
/// </summary>
internal sealed class StartCheckoutHandler(
    BillingDbContext db,
    IProgramCatalog catalog,
    IEntitlements entitlements,
    StripeCatalog stripeCatalog,
    IStripeGateway stripe,
    IOptions<BillingOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>Stripe's minimum session lifetime. Short, so an abandoned checkout can't be paid much later.</summary>
    private static readonly TimeSpan CheckoutLifetime = TimeSpan.FromMinutes(30);

    public async Task<Result<RedirectDto>> Handle(Guid programId, CurrentUser user, string? email, CancellationToken cancellationToken)
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

        if (await entitlements.HasAccessAsync(user.Id, programId, cancellationToken))
        {
            return Error.Conflict("You already have this program.");
        }

        var payout = await db.PayoutAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.TrainerId == program.TrainerId, cancellationToken);

        if (payout is not { IsReady: true })
        {
            return Error.Conflict("This trainer can't take payments right now.");
        }

        // Aspire turns on retries, and a retrying strategy only allows a transaction it runs itself. Stripe calls inside
        // are idempotent or harmless to repeat.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            ct => StartAsync(program, payout, user, email, ct), cancellationToken);
    }

    private async Task<Result<RedirectDto>> StartAsync(
        ProgramSummary program, Domain.PayoutAccount payout, CurrentUser user, string? email, CancellationToken cancellationToken)
    {
        var programId = program.Id;
        db.ChangeTracker.Clear();

        // One checkout start at a time per member and program: a second click waits, then reuses the first one's session.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"checkout:{user.Id}:{programId}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var previous = await db.Purchases
            .Where(p => p.UserId == user.Id && p.ProgramId == programId)
            .ToListAsync(cancellationToken);

        // Reuse an open checkout for the same price instead of opening a second one that could also be paid.
        if (previous.FirstOrDefault(p => p.HasOpenCheckout(now) && p.PriceVersion == program.PriceVersion) is { } open)
        {
            return new RedirectDto(new Uri(open.CheckoutUrl!));
        }

        try
        {
            // Any other session (an older price, or one about to lapse) must not stay payable next to the new one.
            if (!await stripe.CloseAsync(previous, now, cancellationToken))
            {
                return Error.Conflict("Your payment for this program is being processed.");
            }
        }
        catch (Stripe.StripeException ex)
        {
            return ex.ToError();
        }

        var trialEligible = !previous.Any(p => p.Kind == PurchaseKind.Subscription && p.Status != PurchaseStatus.Pending);
        var started = Purchase.Start(user.Id, program, options.Value.PlatformFeePercent, trialEligible, now);

        if (!started.IsSuccess)
        {
            return started.Error!;
        }

        var purchase = started.Value!;

        try
        {
            var customerId = await stripeCatalog.CustomerIdAsync(user.Id, email, cancellationToken);
            var priceId = await stripeCatalog.PriceIdAsync(program.Id, program.Title, program.PriceVersion, program.Price, cancellationToken);
            var programPage = new Uri(options.Value.PublicBaseUrl, $"programs/{programId}");

            db.Purchases.Add(purchase);
            await db.SaveChangesAsync(cancellationToken);

            var session = await stripe.CreateCheckoutAsync(new CheckoutRequest(
                purchase.Id,
                customerId,
                priceId,
                purchase.Kind == PurchaseKind.Subscription,
                purchase.FeeMinor,
                purchase.FeePercent,
                purchase.TrialDays,
                payout.StripeAccountId,
                new Uri(programPage, $"?checkout=success"),
                new Uri(programPage, $"?checkout=cancelled"),
                now.Add(CheckoutLifetime)), cancellationToken);

            try
            {
                purchase.AttachCheckout(session.Id, session.Url, session.ExpiresAt);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                // The purchase rolls back, so a payment on this session would match nothing: make it unpayable.
                await stripe.ExpireCheckoutAsync(session.Id, CancellationToken.None);
                throw;
            }

            return new RedirectDto(new Uri(session.Url));
        }
        catch (Stripe.StripeException ex)
        {
            return ex.ToError();
        }
    }
}

internal static class StartCheckoutEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/programs/{programId:guid}/checkout", async (Guid programId, ClaimsPrincipal user, StartCheckoutHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, CurrentUser.Required(user), user.FindFirstValue("email"), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("StartCheckout");
}
