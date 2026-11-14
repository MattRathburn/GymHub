using FluentValidation;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Features.ArchiveProgram;
using GymHub.Modules.Programs.Features.BrowsePrograms;
using GymHub.Modules.Programs.Features.CreateProgram;
using GymHub.Modules.Programs.Features.GetProgramContent;
using GymHub.Modules.Programs.Features.GetProgramForEditing;
using GymHub.Modules.Programs.Features.GetProgramOverview;
using GymHub.Modules.Programs.Features.ListMyPrograms;
using GymHub.Modules.Programs.Features.ManageMedia;
using GymHub.Modules.Programs.Features.ManageWeeks;
using GymHub.Modules.Programs.Features.ManageWorkouts;
using GymHub.Modules.Programs.Features.PublishProgram;
using GymHub.Modules.Programs.Features.SetProgramPricing;
using GymHub.Modules.Programs.Features.Shared;
using GymHub.Modules.Programs.Features.UpdateProgramDetails;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.Programs;

/// <summary>Trainers author programs (weeks → days → workouts → exercises) that release by calendar date.</summary>
public sealed class ProgramsModule : IModule
{
    public string Name => "programs";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<ProgramsDbContext>(ProgramsDbContext.SchemaName);

        var services = builder.Services;
        services.AddValidatorsFromAssemblyContaining<ProgramsModule>(includeInternalTypes: true);
        services.AddScoped<IProgramCatalog, ProgramCatalog>();
        services.AddScoped<IProgramWorkouts, ProgramWorkouts>();
        services.AddScoped<ProgramContentMapper>();
        services.AddSingleton<WeekReleaseJob>();
        services.AddHostedService(sp => sp.GetRequiredService<WeekReleaseJob>());

        services.AddScoped<CreateProgramHandler>();
        services.AddScoped<UpdateProgramDetailsHandler>();
        services.AddScoped<SetProgramPricingHandler>();
        services.AddScoped<ManageWeeksHandler>();
        services.AddScoped<ManageWorkoutsHandler>();
        services.AddScoped<ManageMediaHandler>();
        services.AddScoped<PublishProgramHandler>();
        services.AddScoped<ArchiveProgramHandler>();
        services.AddScoped<ListMyProgramsHandler>();
        services.AddScoped<GetProgramForEditingHandler>();
        services.AddScoped<BrowseProgramsHandler>();
        services.AddScoped<GetProgramOverviewHandler>();
        services.AddScoped<GetProgramContentHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        BrowseProgramsEndpoint.Map(endpoints);
        GetProgramOverviewEndpoint.Map(endpoints);
        GetProgramContentEndpoint.Map(endpoints);

        ListMyProgramsEndpoint.Map(endpoints);
        GetProgramForEditingEndpoint.Map(endpoints);
        CreateProgramEndpoint.Map(endpoints);
        UpdateProgramDetailsEndpoint.Map(endpoints);
        SetProgramPricingEndpoint.Map(endpoints);
        ManageWeeksEndpoint.Map(endpoints);
        ManageWorkoutsEndpoint.Map(endpoints);
        ManageMediaEndpoint.Map(endpoints);
        PublishProgramEndpoint.Map(endpoints);
        ArchiveProgramEndpoint.Map(endpoints);
    }
}
