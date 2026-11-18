using FluentValidation;
using GymHub.BuildingBlocks.Admin;
using GymHub.BuildingBlocks.Identity;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Outbox;
using GymHub.BuildingBlocks.Persistence;
using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Trainers.Contracts;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Features.EditMyMemberProfile;
using GymHub.Modules.Trainers.Features.EditMyProfile;
using GymHub.Modules.Trainers.Features.FollowTrainer;
using GymHub.Modules.Trainers.Features.GetTrainerProfile;
using GymHub.Modules.Trainers.Features.ListFollowing;
using GymHub.Modules.Trainers.Features.ListTrainers;
using GymHub.Modules.Trainers.Features.ReviewApplications;
using GymHub.Modules.Trainers.Features.RevokeTrainer;
using GymHub.Modules.Trainers.Features.SubmitApplication;
using GymHub.Modules.Trainers.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.Trainers;

/// <summary>Trainer applications and admin review, public trainer profiles, member profiles, and following trainers.</summary>
public sealed class TrainersModule : IModule
{
    public string Name => "trainers";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<TrainersDbContext>(TrainersDbContext.SchemaName);
        builder.Services.AddPersonalDataSource<TrainersPersonalData>();
        builder.Services.AddAdminMetrics<TrainersMetrics>();

        var services = builder.Services;
        services.AddValidatorsFromAssemblyContaining<TrainersModule>(includeInternalTypes: true);

        builder.AddIdentityAdmin();
        services.AddIntegrationEventHandler<TrainerApproved, GrantTrainerRoleHandler>();
        services.AddIntegrationEventHandler<TrainerRevoked, RevokeTrainerRoleHandler>();
        services.AddScoped<ProfileDirectory>();
        services.AddScoped<IProfileDirectory>(sp => sp.GetRequiredService<ProfileDirectory>());
        services.AddScoped<ITrainerDirectory>(sp => sp.GetRequiredService<ProfileDirectory>());
        services.AddScoped<IFollowers, Followers>();
        services.AddSingleton<AvatarSweeper>();
        services.AddHostedService(sp => sp.GetRequiredService<AvatarSweeper>());

        services.AddScoped<SubmitApplicationHandler>();
        services.AddScoped<ReviewApplicationsHandler>();
        services.AddScoped<RevokeTrainerHandler>();
        services.AddScoped<GetTrainerProfileHandler>();
        services.AddScoped<EditMyProfileHandler>();
        services.AddScoped<EditMyMemberProfileHandler>();
        services.AddScoped<FollowTrainerHandler>();
        services.AddScoped<ListFollowingHandler>();
        services.AddScoped<ListTrainersHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        SubmitApplicationEndpoint.Map(endpoints);
        ReviewApplicationsEndpoint.Map(endpoints);
        RevokeTrainerEndpoint.Map(endpoints);
        EditMyProfileEndpoint.Map(endpoints);
        EditMyMemberProfileEndpoint.Map(endpoints);
        ListFollowingEndpoint.Map(endpoints);
        ListTrainersEndpoint.Map(endpoints);
        FollowTrainerEndpoint.Map(endpoints);
        GetTrainerProfileEndpoint.Map(endpoints);
    }
}
