using GymHub.BuildingBlocks.Persistence;
using GymHub.MigrationService;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Trainers.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// One line per module. Each module's migrations live in its own project.
builder.AddModuleDbContext<ProgramsDbContext>(ProgramsDbContext.SchemaName);
builder.AddModuleDbContext<SubscriptionsDbContext>(SubscriptionsDbContext.SchemaName);
builder.AddModuleDbContext<TrainersDbContext>(TrainersDbContext.SchemaName);

builder.Services.AddHostedService<MigrationWorker>();

builder.Build().Run();
