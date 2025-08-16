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

var programMigration = builder.AddProject<Projects.GH_Plan_DbManager>("gh-plan-dbmanager")
        .WithReference(plandb)
        .WaitFor(plandb);


var todoApi = builder.AddProject<Projects.Todo_API>("todo-api")
    .WithReference(keycloak);

var planApi = builder.AddProject<Projects.GH_Plan_API>("gh-plan-api")
    .WithReference(plandb)
    .WithReference(keycloak)
    .WaitFor(programMigration);

//builder.AddNpmApp("gh-webapp-ng", "../GH.WebApp/gh.webapp.ng")
//    .WithReference(keycloak)
//    .WithReference(planApi)
//    .WaitFor(keycloak)
//    //.WithEnvironment("BROWSER", "none")
//    .WithHttpEndpoint(env: "PORT")
//    .WithExternalHttpEndpoints()
//    .PublishAsDockerFile();

builder.AddNpmApp("ghwebappvite", "../GH.WebApp/gh.webapp.vite")
    .WithReference(keycloak)
    .WithReference(planApi)
    .WaitFor(keycloak)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(env: "VITE_PORT")
    .PublishAsDockerFile();

builder.AddProject<Projects.GH_Admin>("gh-admin");

builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(planApi)
    .WaitFor(planApi)
    .WithExternalHttpEndpoints();


builder.Build().Run();
