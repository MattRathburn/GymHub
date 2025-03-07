using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

var postgres = builder.AddPostgres("GymHub")
    .WithImage("ankane/pgvector")
    .WithImageTag("latest")
    .WithPgAdmin();

var identityDb = postgres.AddDatabase("IdentityDB");
var programDb = postgres.AddDatabase("ProgramDB");

//var webAppServer = builder.AddProject<Projects.WebApp_Server>("webapp-server");

var identityApi = builder.AddProject<Projects.GH_Identity_API>("gh-identity-api")
    .WithExternalHttpEndpoints()
    .WithReference(identityDb);

var todoApi = builder.AddProject<Projects.Todo_API>("todo-api")
    .WithExternalHttpEndpoints();

var programApi = builder.AddProject<Projects.GH_Program_API>("gh-program-api")
    .WithExternalHttpEndpoints()
    .WithReference(programDb);

builder.AddNpmApp("GH.WebApp", "../GH.WebApp/gh.webapp")
    .WithReference(identityApi)
    .WithReference(programApi)
    .WaitFor(identityApi)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
