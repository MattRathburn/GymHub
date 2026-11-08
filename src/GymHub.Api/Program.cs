using System.Text.Json.Serialization;
using Asp.Versioning;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Outbox;
using GymHub.BuildingBlocks.Storage;
using GymHub.Modules.Programs;
using GymHub.Modules.Subscriptions;
using GymHub.ServiceDefaults;

IModule[] modules = [new ProgramsModule(), new SubscriptionsModule()];

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication()
    .AddKeycloakJwtBearer("keycloak", realm: "GH-Test-Realm", options =>
    {
        options.RequireHttpsMetadata = false;
        options.Audience = "gymhub-api";
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";
    });
builder.Services.AddGymHubAuthorization();

builder.AddObjectStorage();
builder.Services.AddOutboxDispatcher();

foreach (var module in modules)
{
    module.AddServices(builder);
}

var withApiVersioning = builder.Services.AddApiVersioning(options => options.ApiVersionReader = new UrlSegmentApiVersionReader());
builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
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
