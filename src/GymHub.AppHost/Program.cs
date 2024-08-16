using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

var postgres = builder.AddPostgres("postgres")
    .WithImage("ankane/pgvector")
    .WithImageTag("latest");

var sqlserver = builder.AddSqlServer("sqldb");

var identityDb = sqlserver.AddDatabase("identitydb");

builder.AddProject<Projects.WebApp_Server>("webapp-server");

builder.AddProject<Projects.GH_Identity_API>("gh-identity-api")
    .WithExternalHttpEndpoints()
    .WithReference(identityDb);

builder.AddProject<Projects.Todo_API>("todo-api")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.GH_Program_API>("gh-program-api")
    .WithExternalHttpEndpoints()
    .WithReference(postgres);

builder.Build().Run();
