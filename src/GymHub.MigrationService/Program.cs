using GymHub.BuildingBlocks.Persistence;
using GymHub.MigrationService;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Subscriptions.Data;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.TrainingLog.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// One line per module. Each module's migrations live in its own project.
builder.AddModuleDbContext<ProgramsDbContext>(ProgramsDbContext.SchemaName);
builder.AddModuleDbContext<SubscriptionsDbContext>(SubscriptionsDbContext.SchemaName);
builder.AddModuleDbContext<TrainersDbContext>(TrainersDbContext.SchemaName);
builder.AddModuleDbContext<DiscussionsDbContext>(DiscussionsDbContext.SchemaName);
builder.AddModuleDbContext<BillingDbContext>(BillingDbContext.SchemaName);
builder.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.SchemaName);
builder.AddModuleDbContext<TrainingLogDbContext>(TrainingLogDbContext.SchemaName);

builder.Services.AddHostedService<MigrationWorker>();

builder.Build().Run();
