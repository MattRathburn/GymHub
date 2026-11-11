using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Outbox;
using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Features.ListPurchases;
using GymHub.Modules.Billing.Features.OpenPortal;
using GymHub.Modules.Billing.Features.Payouts;
using GymHub.Modules.Billing.Features.RefundPurchase;
using GymHub.Modules.Billing.Features.Shared;
using GymHub.Modules.Billing.Features.StartCheckout;
using GymHub.Modules.Billing.Features.StripeWebhook;
using GymHub.Modules.Billing.Payments;
using GymHub.Modules.Billing.PriceChanges;
using GymHub.Modules.Programs.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.Billing;

/// <summary>
/// Payments through Stripe Connect: trainer payout accounts (Express), Checkout for one-time and recurring
/// purchases, the Customer Portal, refunds, and the Stripe webhook that grants and ends paid access.
/// </summary>
public sealed class BillingModule : IModule
{
    public string Name => "billing";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<BillingDbContext>(BillingDbContext.SchemaName);

        var services = builder.Services;
        services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.SectionName));

        // Created on first use, so the API starts without Stripe keys (paid features then fail with a clear error).
        services.AddSingleton<IStripeGateway, StripeGateway>();
        services.AddScoped<StripeCatalog>();
        services.AddScoped<IPayoutReadiness, PayoutReadiness>();

        services.AddIntegrationEventHandler<ProgramPricingChanged, ProgramPricingChangedHandler>();
        services.AddIntegrationEventHandler<ProgramArchived, ProgramArchivedHandler>();

        services.AddScoped<PayoutsHandler>();
        services.AddScoped<StartCheckoutHandler>();
        services.AddScoped<OpenPortalHandler>();
        services.AddScoped<ListPurchasesHandler>();
        services.AddScoped<RefundPurchaseHandler>();
        services.AddScoped<StripeWebhookHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PayoutsEndpoint.Map(endpoints);
        StartCheckoutEndpoint.Map(endpoints);
        OpenPortalEndpoint.Map(endpoints);
        ListPurchasesEndpoint.Map(endpoints);
        RefundPurchaseEndpoint.Map(endpoints);
        StripeWebhookEndpoint.Map(endpoints);
    }
}
