using FluentValidation;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.TrainingLog.Data;
using GymHub.Modules.TrainingLog.Features.BrowseSessions;
using GymHub.Modules.TrainingLog.Features.EditSession;
using GymHub.Modules.TrainingLog.Features.Exercises;
using GymHub.Modules.TrainingLog.Features.LogSession;
using GymHub.Modules.TrainingLog.Features.ProgramAdherence;
using GymHub.Modules.TrainingLog.Features.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.TrainingLog;

/// <summary>
/// Members log the workouts they did (freestyle or following a program) and see their history, per-exercise progress,
/// personal records and how much of a program they've done. Logs are private to the member.
/// </summary>
public sealed class TrainingLogModule : IModule
{
    public string Name => "training";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<TrainingLogDbContext>(TrainingLogDbContext.SchemaName);

        var services = builder.Services;
        services.AddValidatorsFromAssemblyContaining<TrainingLogModule>(includeInternalTypes: true);
        services.AddScoped<ProgramAccess>();
        services.AddScoped<SessionRecords>();

        services.AddScoped<LogSessionHandler>();
        services.AddScoped<EditSessionHandler>();
        services.AddScoped<BrowseSessionsHandler>();
        services.AddScoped<ExercisesHandler>();
        services.AddScoped<ProgramAdherenceHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        BrowseSessionsEndpoint.Map(endpoints);
        LogSessionEndpoint.Map(endpoints);
        EditSessionEndpoint.Map(endpoints);
        ExercisesEndpoint.Map(endpoints);
        ProgramAdherenceEndpoint.Map(endpoints);
    }
}
