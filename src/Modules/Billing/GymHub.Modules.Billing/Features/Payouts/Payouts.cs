using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Features.Shared;
using GymHub.Modules.Billing.Payments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Billing.Features.Payouts;

public enum PayoutSetup
{
    NotStarted,
    InProgress,
    Ready
}

public sealed record PayoutStatusDto(PayoutSetup Setup, bool ChargesEnabled, bool PayoutsEnabled, bool DetailsSubmitted, decimal PlatformFeePercent);

/// <summary>Trainers connect a Stripe Express account to get paid, and open its dashboard.</summary>
internal sealed class PayoutsHandler(BillingDbContext db, IStripeGateway stripe, IOptions<BillingOptions> options, TimeProvider timeProvider)
{
    private const string PayoutsPage = "studio/payouts";

    /// <summary>Also refreshes the account from Stripe, so returning from onboarding shows the result without waiting for the webhook.</summary>
    public async Task<PayoutStatusDto> Status(CurrentUser user, CancellationToken cancellationToken)
    {
        var account = await db.PayoutAccounts.SingleOrDefaultAsync(a => a.TrainerId == user.Id, cancellationToken);

        if (account is null)
        {
            return new PayoutStatusDto(PayoutSetup.NotStarted, false, false, false, options.Value.PlatformFeePercent);
        }

        if (!account.IsReady || !account.PayoutsEnabled)
        {
            try
            {
                var status = await stripe.GetAccountStatusAsync(account.StripeAccountId, cancellationToken);
                account.Update(status.ChargesEnabled, status.PayoutsEnabled, status.DetailsSubmitted, timeProvider.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Stripe.StripeException)
            {
                // Show what we last knew; the account.updated webhook will catch up.
            }
        }

        return new PayoutStatusDto(
            account.IsReady ? PayoutSetup.Ready : PayoutSetup.InProgress,
            account.ChargesEnabled, account.PayoutsEnabled, account.DetailsSubmitted, options.Value.PlatformFeePercent);
    }

    public async Task<Result<RedirectDto>> StartOnboarding(CurrentUser user, string? email, CancellationToken cancellationToken)
    {
        try
        {
            var account = await db.PayoutAccounts.SingleOrDefaultAsync(a => a.TrainerId == user.Id, cancellationToken);

            if (account is null)
            {
                var accountId = await stripe.CreateExpressAccountAsync(user.Id, email, cancellationToken);
                account = new PayoutAccount(user.Id, accountId, timeProvider.GetUtcNow().UtcDateTime);
                db.PayoutAccounts.Add(account);
                await db.SaveChangesAsync(cancellationToken);
            }

            var page = new Uri(options.Value.PublicBaseUrl, PayoutsPage);
            var link = await stripe.CreateOnboardingLinkAsync(
                account.StripeAccountId, new Uri(page, "?onboarding=refresh"), new Uri(page, "?onboarding=done"), cancellationToken);

            return new RedirectDto(link);
        }
        catch (Stripe.StripeException ex)
        {
            return ex.ToError();
        }
    }

    public async Task<Result<RedirectDto>> Dashboard(CurrentUser user, CancellationToken cancellationToken)
    {
        var account = await db.PayoutAccounts.SingleOrDefaultAsync(a => a.TrainerId == user.Id, cancellationToken);

        if (account is not { DetailsSubmitted: true })
        {
            return Error.Conflict("Finish setting up payouts first.");
        }

        try
        {
            return new RedirectDto(await stripe.CreateDashboardLinkAsync(account.StripeAccountId, cancellationToken));
        }
        catch (Stripe.StripeException ex)
        {
            return ex.ToError();
        }
    }
}

internal static class PayoutsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var payouts = app.MapGroup("/payouts").RequireAuthorization(Policies.Trainer);

        payouts.MapGet("", async (ClaimsPrincipal user, PayoutsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Status(CurrentUser.Required(user), ct)))
            .WithName("GetPayoutStatus");

        payouts.MapPost("/onboarding", async (ClaimsPrincipal user, PayoutsHandler handler, CancellationToken ct) =>
                (await handler.StartOnboarding(CurrentUser.Required(user), user.FindFirstValue("email"), ct)).ToHttp())
            .WithName("StartPayoutOnboarding");

        payouts.MapPost("/dashboard", async (ClaimsPrincipal user, PayoutsHandler handler, CancellationToken ct) =>
                (await handler.Dashboard(CurrentUser.Required(user), ct)).ToHttp())
            .WithName("OpenPayoutDashboard");
    }
}
