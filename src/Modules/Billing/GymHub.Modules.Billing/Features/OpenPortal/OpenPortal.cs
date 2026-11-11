using System.Security.Claims;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Features.Shared;
using GymHub.Modules.Billing.Payments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Billing.Features.OpenPortal;

/// <summary>Stripe's Customer Portal, where members cancel subscriptions, update cards and see invoices.</summary>
internal sealed class OpenPortalHandler(BillingDbContext db, IStripeGateway stripe, IOptions<BillingOptions> options)
{
    public async Task<Result<RedirectDto>> Handle(CurrentUser user, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

        if (customer is null)
        {
            return Error.NotFound("You haven't bought anything yet.");
        }

        try
        {
            var returnUrl = new Uri(options.Value.PublicBaseUrl, "me/programs");
            return new RedirectDto(await stripe.CreatePortalSessionAsync(customer.StripeCustomerId, returnUrl, cancellationToken));
        }
        catch (Stripe.StripeException ex)
        {
            return ex.ToError();
        }
    }
}

internal static class OpenPortalEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/portal", async (ClaimsPrincipal user, OpenPortalHandler handler, CancellationToken ct) =>
                (await handler.Handle(CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization()
            .WithName("OpenBillingPortal");
}
