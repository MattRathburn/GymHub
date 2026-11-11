using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Outbox;
using GymHub.Modules.Billing.Contracts;
using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Subscriptions.Features.Enroll;
using GymHub.Modules.Subscriptions.Features.ListMyEnrollments;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.Subscriptions;

/// <summary>Who has access to which program: free enrollments, and paid ones driven by Billing's purchase events.</summary>
public sealed class SubscriptionsModule : IModule
{
    public string Name => "subscriptions";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<SubscriptionsDbContext>(SubscriptionsDbContext.SchemaName);

        builder.Services.AddScoped<IEntitlements, Entitlements>();
        builder.Services.AddScoped<EnrollHandler>();
        builder.Services.AddScoped<ListMyEnrollmentsHandler>();
        builder.Services.AddIntegrationEventHandler<PaidAccessGranted, PaidAccessGrantedHandler>();
        builder.Services.AddIntegrationEventHandler<PaidAccessEnded, PaidAccessEndedHandler>();
        builder.Services.AddIntegrationEventHandler<PaidAccessConvertedToFree, PaidAccessConvertedToFreeHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        EnrollEndpoint.Map(endpoints);
        ListMyEnrollmentsEndpoint.Map(endpoints);
    }
}
