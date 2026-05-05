using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume();

var postgres = builder.AddPostgres("gymhub")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

//if (builder.ExecutionContext.IsRunMode)
//{
//    postgres.WithDataVolume();
//}

var plandb = postgres.AddDatabase("plandb");

var planCache = builder.AddRedis("plancache")
    .WithDataVolume()
    .WithRedisInsight();

var todoApi = builder.AddProject<Projects.Todo_API>("todo-api")
    .WithReference(keycloak);

var planApi = builder.AddProject<Projects.GH_Plan_API>("gh-plan-api")
    .WithReference(plandb)
    .WithReference(keycloak);

builder.AddNpmApp("gh-webapp-ng", "../GH.WebApp/gh.webapp.ng")
    .WithReference(keycloak)
    .WithReference(planApi)
    .WaitFor(keycloak)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.AddProject<Projects.GH_Admin>("gh-admin");

builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(planApi)
    .WithReference(keycloak)
    .WaitFor(planApi)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();


builder.Build().Run();
