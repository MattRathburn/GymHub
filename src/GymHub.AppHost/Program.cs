using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// Shared by Keycloak's realm import (Realms/gh-test-realm.json) and the Gateway. Set it in this project's user-secrets.
var bffClientSecret = builder.AddParameter("bff-client-secret", secret: true);

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume("gymhub-keycloak-data")
    .WithRealmImport("./Realms")
    .WithEnvironment("GYMHUB_BFF_CLIENT_SECRET", bffClientSecret);

var postgres = builder.AddPostgres("gymhub")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

var gymhubDb = postgres.AddDatabase("gymhubdb");

var media = builder.AddMinioContainer("media")
    .WithDataVolume();

builder.AddRedis("plancache")
    .WithDataVolume()
    .WithRedisInsight();

var migrations = builder.AddProject<Projects.GymHub_MigrationService>("gymhub-migrations")
    .WithReference(gymhubDb)
    .WaitFor(gymhubDb);

var api = builder.AddProject<Projects.GymHub_Api>("gymhub-api")
    .WithReference(gymhubDb)
    .WithReference(keycloak)
    .WithReference(media)
    .WaitFor(media)
    .WaitForCompletion(migrations);

var webApp = builder.AddNpmApp("gh-webapp-ng", "../GH.WebApp/gh.webapp.ng")
    .WithHttpEndpoint(env: "PORT")
    .PublishAsDockerFile();

builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(api)
    .WithReference(keycloak)
    .WithReference(webApp)
    .WithEnvironment("OpenIdConnect__ClientSecret", bffClientSecret)
    .WaitFor(api)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();

builder.Build().Run();
