using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// Shared by Keycloak's realm import (Realms/gh-test-realm.json) and the Gateway. Set it in this project's user-secrets.
var bffClientSecret = builder.AddParameter("bff-client-secret", secret: true);

// Shared by the realm import's gymhub-api-admin service account and the API, which grants the trainer role with it.
var apiAdminClientSecret = builder.AddParameter("api-admin-client-secret", secret: true);

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume("gymhub-keycloak")
    .WithRealmImport("./Realms")
    .WithEnvironment("GYMHUB_BFF_CLIENT_SECRET", bffClientSecret)
    .WithEnvironment("GYMHUB_API_ADMIN_CLIENT_SECRET", apiAdminClientSecret)
    // One issuer for everyone: the browser/Gateway use localhost:8080, the API reaches Keycloak through service discovery.
    .WithEnvironment("KC_HOSTNAME", "http://localhost:8080")
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true");

var postgres = builder.AddPostgres("gymhub")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

var gymhubDb = postgres.AddDatabase("gymhubdb");

// S3-compatible media storage (RustFS; MinIO no longer publishes community images). Keys live in user-secrets.
var mediaAccessKey = builder.AddParameter("media-access-key", secret: true);
var mediaSecretKey = builder.AddParameter("media-secret-key", secret: true);

var mediaStore = builder.AddContainer("media-store", "rustfs/rustfs", "1.0.4")
    .WithHttpEndpoint(targetPort: 9000, name: "s3")
    .WithHttpEndpoint(targetPort: 9001, name: "console")
    .WithEnvironment("RUSTFS_ACCESS_KEY", mediaAccessKey)
    .WithEnvironment("RUSTFS_SECRET_KEY", mediaSecretKey)
    .WithVolume("gymhub-media", "/data");

var media = builder.AddConnectionString("media", ReferenceExpression.Create(
    $"Endpoint={mediaStore.GetEndpoint("s3")};AccessKey={mediaAccessKey};SecretKey={mediaSecretKey}"));

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
    .WithEnvironment("KeycloakAdmin__ClientSecret", apiAdminClientSecret)
    .WaitFor(mediaStore)
    .WaitForCompletion(migrations);

var webApp = builder.AddNpmApp("gh-webapp-ng", "../GH.WebApp/gh.webapp.ng")
    .WithHttpEndpoint(env: "PORT")
    .PublishAsDockerFile();

var gateway = builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(api)
    .WithReference(keycloak)
    .WithReference(webApp)
    .WithEnvironment("OpenIdConnect__ClientSecret", bffClientSecret)
    .WaitFor(api)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();

// Browsers upload and download media straight from storage with presigned URLs, from the Gateway's origin.
// (Built from the host port: inside the container the endpoint itself resolves to host.docker.internal.)
mediaStore.WithEnvironment("RUSTFS_CORS_ALLOWED_ORIGINS", ReferenceExpression.Create(
    $"https://localhost:{gateway.GetEndpoint("https").Property(EndpointProperty.Port)}"));

builder.Build().Run();
