using System.Text.Json.Serialization;
using Asp.Versioning;
using GymHub.Api;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Outbox;
using GymHub.BuildingBlocks.ScaleOut;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Accounts;
using GymHub.Modules.Billing;
using GymHub.Modules.Discussions;
using GymHub.Modules.Notifications;
using GymHub.Modules.Programs;
using GymHub.Modules.Reviews;
using GymHub.Modules.Subscriptions;
using GymHub.Modules.Trainers;
using GymHub.Modules.TrainingLog;
using GymHub.ServiceDefaults;

IModule[] modules = [new ProgramsModule(), new SubscriptionsModule(), new TrainersModule(), new DiscussionsModule(), new BillingModule(), new NotificationsModule(), new TrainingLogModule(), new ReviewsModule(), new AccountsModule()];

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var realm = builder.Configuration["Keycloak:Realm"]
    ?? throw new InvalidOperationException("Keycloak:Realm is not configured.");

builder.Services.AddAuthentication()
    .AddKeycloakJwtBearer("keycloak", realm, options =>
    {
        // Local Keycloak runs on plain http; anywhere else the metadata must come over https, unless the deployment
        // says the API reaches Keycloak on a private network (Keycloak:RequireHttpsMetadata = false, set by the
        // published AppHost, where service discovery gives http://keycloak inside the environment). Tokens are
        // still checked against the realm's https issuer and signing keys either way.
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Keycloak:RequireHttpsMetadata", !builder.Environment.IsDevelopment());
        options.Audience = "gymhub-api";
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";
    });
builder.Services.AddGymHubAuthorization();
builder.AddApiRateLimiting();

builder.AddObjectStorage();
builder.AddScaleOut();
builder.Services.AddOutboxDispatcher();

foreach (var module in modules)
{
    module.AddServices(builder);
}

var withApiVersioning = builder.Services.AddApiVersioning(options => options.ApiVersionReader = new UrlSegmentApiVersionReader());
builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

app.UseDefaultForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAccountGuard();
app.UseAuthorization();
app.MapDefaultEndpoints();

var api = app.NewVersionedApi("GymHub")
    .MapGroup("/api/v{version:apiVersion}")
    .HasApiVersion(1.0);

foreach (var module in modules)
{
    module.MapEndpoints(api.MapGroup($"/{module.Name}").WithTags(module.Name));
}

app.UseDefaultOpenApi();

app.Run();

/// <summary>Entry point, public for WebApplicationFactory in GH.FunctionalTests.</summary>
public partial class Program;
