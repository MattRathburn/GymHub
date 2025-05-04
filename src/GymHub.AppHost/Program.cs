using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithExternalHttpEndpoints();

var postgres = builder.AddPostgres("gymhub")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

//if (builder.ExecutionContext.IsRunMode)
//{
//    postgres.WithDataVolume();
//}

var programdb = postgres.AddDatabase("programdb");

var programCache = builder.AddRedis("programcache")
    .WithDataVolume()
    .WithRedisInsight();

var programMigration = builder.AddProject<Projects.GH_Program_DbManager>("gh-program-dbmanager")
        .WithReference(programdb)
        .WaitFor(programdb);


var todoApi = builder.AddProject<Projects.Todo_API>("todo-api")
    .WithReference(keycloak)
    .WithExternalHttpEndpoints();

var programApi = builder.AddProject<Projects.GH_Program_API>("gh-program-api")
    .WithExternalHttpEndpoints()
    .WithReference(programdb)
    .WithReference(keycloak)
    .WaitFor(programMigration);

builder.AddNpmApp("ghwebapp", "../GH.WebApp/gh.webapp")
    .WithReference(keycloak)
    .WithReference(programApi)
    .WaitFor(keycloak)
    //.WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

//builder.AddNpmApp("ghwebappvite", "../GH.WebApp/gh.webapp.vite")
//    .WithReference(keycloak)
//    .WithReference(programApi)
//    .WaitFor(keycloak)
//    .WithEnvironment("BROWSER", "none")
//    .WithHttpEndpoint(env: "VITE_PORT")
//    .WithExternalHttpEndpoints()
//    .PublishAsDockerFile();



builder.AddProject<Projects.GH_Admin>("gh-admin");

//builder.AddNpmApp("ghwebappvite", "../GH.WebApp/gh.webapp.vite")
//    .WithReference(keycloak)
//    .WithReference(programApi)
//    .WaitFor(keycloak)
//    .WithEnvironment("BROWSER", "none")
//    .WithHttpEndpoint(env: "VITE_PORT")
//    .WithExternalHttpEndpoints()
//    .PublishAsDockerFile();



builder.Build().Run();
