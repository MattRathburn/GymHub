using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Billing.Features.Shared;
using GymHub.Modules.Billing.Payments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Billing.Features.RefundPurchase;

/// <summary>
/// Full refund of the latest payment. The trainer can refund their own sales within the refund window; admins any
/// time. Access ends at once, and a refunded subscription is cancelled.
/// </summary>
internal sealed class RefundPurchaseHandler(BillingDbContext db, IStripeGateway stripe, IOptions<BillingOptions> options, TimeProvider timeProvider)
{
    public async Task<Result> Handle(Guid purchaseId, CurrentUser user, CancellationToken cancellationToken)
    {
        var purchase = await db.Purchases.SingleOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);

        if (purchase is null || (!user.IsAdmin && purchase.TrainerId != user.Id))
        {
            return Error.NotFound("Purchase not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var allowed = purchase.CanBeRefundedBy(user, options.Value.TrainerRefundWindowDays, now);

        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        try
        {
            // Cancel first: if the refund then fails, retrying is safe (both calls are idempotent), whereas a refund
            // whose cancel failed would leave a refunded subscription still renewing.
            if (purchase.Kind == PurchaseKind.Subscription && purchase.SubscriptionId is { } subscriptionId)
            {
                await stripe.CancelNowAsync(subscriptionId, cancellationToken);
            }

            await stripe.RefundAsync(purchase.PaymentIntentId!, purchase.Id, cancellationToken);
        }
        catch (Stripe.StripeException ex)
        {
            return ex.ToError();
        }

        purchase.MarkRefunded(user.Id, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // A webhook (e.g. the cancellation just made) updated the purchase meanwhile: apply the refund on top of it.
            await ex.Entries.Single().ReloadAsync(cancellationToken);
            purchase.MarkRefunded(user.Id, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

internal static class RefundPurchaseEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/purchases/{purchaseId:guid}/refund", async (Guid purchaseId, ClaimsPrincipal user, RefundPurchaseHandler handler, CancellationToken ct) =>
                (await handler.Handle(purchaseId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization(Policies.Trainer)
            .WithName("RefundPurchase");
}
