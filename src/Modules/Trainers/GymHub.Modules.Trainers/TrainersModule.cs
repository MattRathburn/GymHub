using FluentValidation;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Outbox;
using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Trainers.Contracts;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Features.EditMyProfile;
using GymHub.Modules.Trainers.Features.FollowTrainer;
using GymHub.Modules.Trainers.Features.GetTrainerProfile;
using GymHub.Modules.Trainers.Features.ListFollowing;
using GymHub.Modules.Trainers.Features.ReviewApplications;
using GymHub.Modules.Trainers.Features.SubmitApplication;
using GymHub.Modules.Trainers.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GymHub.Modules.Trainers;

/// <summary>Trainer applications and admin review, public trainer profiles, and following trainers.</summary>
public sealed class TrainersModule : IModule
{
    public string Name => "trainers";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<TrainersDbContext>(TrainersDbContext.SchemaName);

        var services = builder.Services;
        services.AddValidatorsFromAssemblyContaining<TrainersModule>(includeInternalTypes: true);

        services.Configure<KeycloakAdminOptions>(builder.Configuration.GetSection(KeycloakAdminOptions.SectionName));
        services.AddHttpClient<IIdentityAdmin, KeycloakIdentityAdmin>((sp, http) =>
            http.BaseAddress = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value.BaseAddress);
        services.AddIntegrationEventHandler<TrainerApproved, GrantTrainerRoleHandler>();

        services.AddScoped<SubmitApplicationHandler>();
        services.AddScoped<ReviewApplicationsHandler>();
        services.AddScoped<GetTrainerProfileHandler>();
        services.AddScoped<EditMyProfileHandler>();
        services.AddScoped<FollowTrainerHandler>();
        services.AddScoped<ListFollowingHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        SubmitApplicationEndpoint.Map(endpoints);
        ReviewApplicationsEndpoint.Map(endpoints);
        EditMyProfileEndpoint.Map(endpoints);
        ListFollowingEndpoint.Map(endpoints);
        FollowTrainerEndpoint.Map(endpoints);
        GetTrainerProfileEndpoint.Map(endpoints);
    }
}
