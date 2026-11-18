using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// Shared by Keycloak's realm import (Realms/gh-test-realm.json) and the Gateway. Set it in this project's user-secrets.
var bffClientSecret = builder.AddParameter("bff-client-secret", secret: true);

// Shared by the realm import's gymhub-api-admin service account and the API, which grants the trainer role with it.
var apiAdminClientSecret = builder.AddParameter("api-admin-client-secret", secret: true);

// Catches every email locally (notifications and Keycloak's verification and password-reset mails); its web UI lists them. No secrets needed.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.27")
    .WithEndpoint(targetPort: 1025, name: "smtp")
    .WithHttpEndpoint(targetPort: 8025, name: "http");

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume("gymhub-keycloak")
    .WithRealmImport("./Realms")
    .WithEnvironment("GYMHUB_BFF_CLIENT_SECRET", bffClientSecret)
    .WithEnvironment("GYMHUB_API_ADMIN_CLIENT_SECRET", apiAdminClientSecret)
    // Used by the realm import's smtpServer, so Keycloak sends sign-up verification and password resets to Mailpit.
    .WithEnvironment("GYMHUB_SMTP_HOST", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("GYMHUB_SMTP_PORT", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
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

// Lets several API instances work together: the SignalR backplane and instance messages. Without it each instance
// keeps these to itself.
var redis = builder.AddRedis("redis");

// The Gateway's own Redis: sessions (users' tokens) and Data Protection keys. Separate from the API's so an API
// instance never holds credentials to it.
var gatewayRedis = builder.AddRedis("gateway-redis");

var migrations = builder.AddProject<Projects.GymHub_MigrationService>("gymhub-migrations")
    .WithReference(gymhubDb)
    .WaitFor(gymhubDb);

var api = builder.AddProject<Projects.GymHub_Api>("gymhub-api")
    .WithReference(gymhubDb)
    .WithReference(keycloak)
    .WithReference(media)
    .WithReference(redis)
    .WithEnvironment("KeycloakAdmin__ClientSecret", apiAdminClientSecret)
    .WithEnvironment("Notifications__Smtp__Host", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Notifications__Smtp__Port", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WithEnvironment("Notifications__EmailUnverifiedAddresses", "true")
    .WaitFor(mailpit)
    .WaitFor(mediaStore)
    .WaitFor(redis)
    .WaitForCompletion(migrations);

var gateway = builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(api)
    .WithReference(keycloak)
    .WithReference(gatewayRedis)
    .WaitFor(gatewayRedis)
    .WithEnvironment("OpenIdConnect__ClientSecret", bffClientSecret)
    // The CSP lets the browser load and upload media from storage (presigned URLs).
    .WithEnvironment("Gateway__Csp__MediaOrigins__0", mediaStore.GetEndpoint("s3"))
    .WaitFor(api)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();

// Locally the Gateway proxies to the Angular dev server. A published Gateway carries the built app itself
// (dotnet publish runs the Angular build), so there is no web app resource to deploy.
if (builder.ExecutionContext.IsRunMode)
{
    var webApp = builder.AddNpmApp("gh-webapp-ng", "../GH.WebApp/gh.webapp.ng")
        .WithHttpEndpoint(env: "PORT");

    // The catch-all YARP route to the dev server (lowest priority, after the API and Gateway endpoints).
    gateway.WithReference(webApp)
        .WithEnvironment("ReverseProxy__Routes__spa__ClusterId", "gh-webapp-ng")
        .WithEnvironment("ReverseProxy__Routes__spa__Order", "1000")
        .WithEnvironment("ReverseProxy__Routes__spa__Match__Path", "{**catch-all}")
        .WithEnvironment("ReverseProxy__Clusters__gh-webapp-ng__Destinations__gh-webapp-ng__Address", "http://gh-webapp-ng");
}

// Stripe (Billing). Optional: without stripe-secret-key in user-secrets the app runs and paid features report that
// payments aren't configured. stripe-webhook-secret is the whsec_ value printed by `stripe listen`; without it,
// checkout works but webhooks are rejected, so purchases never grant access.
api.WithEnvironment("Billing__PublicBaseUrl", gateway.GetEndpoint("https"))
    .WithEnvironment("Notifications__PublicBaseUrl", gateway.GetEndpoint("https"));

if (!string.IsNullOrEmpty(builder.Configuration["Parameters:stripe-secret-key"]))
{
    api.WithEnvironment("Billing__StripeSecretKey", builder.AddParameter("stripe-secret-key", secret: true));
}

if (!string.IsNullOrEmpty(builder.Configuration["Parameters:stripe-webhook-secret"]))
{
    api.WithEnvironment("Billing__StripeWebhookSecret", builder.AddParameter("stripe-webhook-secret", secret: true));
}

// Browsers upload and download media straight from storage with presigned URLs, from the Gateway's origin.
// (Built from the host port: inside the container the endpoint itself resolves to host.docker.internal.)
mediaStore.WithEnvironment("RUSTFS_CORS_ALLOWED_ORIGINS", ReferenceExpression.Create(
    $"https://localhost:{gateway.GetEndpoint("https").Property(EndpointProperty.Port)}"));

builder.Build().Run();
