using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var isPublish = builder.ExecutionContext.IsPublishMode;

// Shared by Keycloak's realm import and the Gateway. Set it in this project's user-secrets.
var bffClientSecret = builder.AddParameter("bff-client-secret", secret: true);

// Shared by the realm import's gymhub-api-admin service account and the API, which grants the trainer role with it.
var apiAdminClientSecret = builder.AddParameter("api-admin-client-secret", secret: true);

var postgres = builder.AddPostgres("gymhub")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

var gymhubDb = postgres.AddDatabase("gymhubdb");

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithEnvironment("GYMHUB_BFF_CLIENT_SECRET", bffClientSecret)
    .WithEnvironment("GYMHUB_API_ADMIN_CLIENT_SECRET", apiAdminClientSecret)
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true");

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
    .WithReference(redis)
    .WithEnvironment("KeycloakAdmin__ClientSecret", apiAdminClientSecret)
    .WaitFor(redis)
    .WaitForCompletion(migrations);

var gateway = builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(api)
    .WithReference(keycloak)
    .WithReference(gatewayRedis)
    .WaitFor(gatewayRedis)
    .WithEnvironment("OpenIdConnect__ClientSecret", bffClientSecret)
    .WaitFor(api)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();

if (isPublish)
{
    AddProductionDependencies();
}
else
{
    AddLocalDependencies();
}

builder.Build().Run();

// Local runs: Mailpit, RustFS, the test realm with its seeded users, and the Angular dev server.
void AddLocalDependencies()
{
    // Catches every email (notifications and Keycloak's verification and password-reset mails); its web UI lists them. No secrets needed.
    var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.27")
        .WithEndpoint(targetPort: 1025, name: "smtp")
        .WithHttpEndpoint(targetPort: 8025, name: "http");

    keycloak.WithDataVolume("gymhub-keycloak")
        .WithRealmImport("./Realms")
        // Used by the realm import's smtpServer, so Keycloak sends sign-up verification and password resets to Mailpit.
        .WithEnvironment("GYMHUB_SMTP_HOST", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
        .WithEnvironment("GYMHUB_SMTP_PORT", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
        // One issuer for everyone: the browser/Gateway use localhost:8080, the API reaches Keycloak through service discovery.
        .WithEnvironment("KC_HOSTNAME", "http://localhost:8080");

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

    api.WithReference(media)
        .WithEnvironment("Notifications__Smtp__Host", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
        .WithEnvironment("Notifications__Smtp__Port", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
        .WithEnvironment("Notifications__EmailUnverifiedAddresses", "true")
        .WithEnvironment("Billing__PublicBaseUrl", gateway.GetEndpoint("https"))
        .WithEnvironment("Notifications__PublicBaseUrl", gateway.GetEndpoint("https"))
        .WaitFor(mailpit)
        .WaitFor(mediaStore);

    // The CSP lets the browser load and upload media from storage (presigned URLs).
    gateway.WithEnvironment("Gateway__Csp__MediaOrigins__0", mediaStore.GetEndpoint("s3"));

    // Browsers upload and download media straight from storage with presigned URLs, from the Gateway's origin.
    // (Built from the host port: inside the container the endpoint itself resolves to host.docker.internal.)
    mediaStore.WithEnvironment("RUSTFS_CORS_ALLOWED_ORIGINS", ReferenceExpression.Create(
        $"https://localhost:{gateway.GetEndpoint("https").Property(EndpointProperty.Port)}"));

    // Stripe (Billing). Optional: without stripe-secret-key in user-secrets the app runs and paid features report that
    // payments aren't configured. stripe-webhook-secret is the whsec_ value printed by `stripe listen`; without it,
    // checkout works but webhooks are rejected, so purchases never grant access.
    if (!string.IsNullOrEmpty(builder.Configuration["Parameters:stripe-secret-key"]))
    {
        api.WithEnvironment("Billing__StripeSecretKey", builder.AddParameter("stripe-secret-key", secret: true));
    }

    if (!string.IsNullOrEmpty(builder.Configuration["Parameters:stripe-webhook-secret"]))
    {
        api.WithEnvironment("Billing__StripeWebhookSecret", builder.AddParameter("stripe-webhook-secret", secret: true));
    }

    // Locally the Gateway proxies to the Angular dev server. A published Gateway carries the built app itself
    // (dotnet publish runs the Angular build), so there is no web app resource to deploy.
    var webApp = builder.AddNpmApp("gh-webapp-ng", "../GH.WebApp/gh.webapp.ng")
        .WithHttpEndpoint(env: "PORT");

    // The catch-all YARP route to the dev server (lowest priority, after the API and Gateway endpoints).
    gateway.WithReference(webApp)
        .WithEnvironment("ReverseProxy__Routes__spa__ClusterId", "gh-webapp-ng")
        .WithEnvironment("ReverseProxy__Routes__spa__Order", "1000")
        .WithEnvironment("ReverseProxy__Routes__spa__Match__Path", "{**catch-all}")
        .WithEnvironment("ReverseProxy__Clusters__gh-webapp-ng__Destinations__gh-webapp-ng__Address", "http://gh-webapp-ng");
}

// Production (publish mode): the GymHub realm without test users, Keycloak on Postgres, SendGrid SMTP, Cloudflare R2.
// Every value comes from a parameter; docs/production-dependencies.md lists them.
void AddProductionDependencies()
{
    const string realm = "GymHub";

    // Host names only, e.g. "gymhub.example" and "auth.gymhub.example"; both are served over https by the ingress.
    var publicHost = builder.AddParameter("public-host");
    var authHost = builder.AddParameter("auth-host");
    var publicOrigin = ReferenceExpression.Create($"https://{publicHost}");
    var authOrigin = ReferenceExpression.Create($"https://{authHost}");

    // SendGrid: the user is literally "apikey" and the password is the API key.
    var smtpHost = builder.AddParameter("smtp-host", "smtp.sendgrid.net");
    var smtpPort = builder.AddParameter("smtp-port", "587");
    var smtpUser = builder.AddParameter("smtp-username", "apikey");
    var smtpPassword = builder.AddParameter("smtp-password", secret: true);
    var emailFrom = builder.AddParameter("email-from");

    // Cloudflare R2: Endpoint=https://<account>.r2.cloudflarestorage.com;AccessKey=...;SecretKey=...;Bucket=...;Region=auto
    var media = builder.AddConnectionString("media");
    // The browser loads and uploads media there (presigned URLs): https://<account>.r2.cloudflarestorage.com
    var mediaOrigin = builder.AddParameter("media-origin");

    // Keycloak's own database and role, created by the migration service: Keycloak never gets the server's admin
    // credentials, so it can't touch gymhubdb.
    const string keycloakDatabase = "keycloak";
    var keycloakDbPassword = builder.AddParameter("keycloak-db-password", secret: true);

    migrations.WithEnvironment("KeycloakDatabase__Name", keycloakDatabase)
        .WithEnvironment("KeycloakDatabase__Password", keycloakDbPassword);

    keycloak.WithDockerfile("./Keycloak")
        .WithEnvironment("KC_DB_URL_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
        .WithEnvironment("KC_DB_URL_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.TargetPort))
        .WithEnvironment("KC_DB_URL_DATABASE", keycloakDatabase)
        .WithEnvironment("KC_DB_USERNAME", keycloakDatabase)
        .WithEnvironment("KC_DB_PASSWORD", keycloakDbPassword)
        // TLS ends at the ingress; Keycloak trusts its X-Forwarded-* headers and builds every URL from KC_HOSTNAME.
        .WithEnvironment("KC_HTTP_ENABLED", "true")
        .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
        .WithEnvironment("KC_HOSTNAME", authOrigin)
        // Read by the realm import's placeholders (redirect URIs and SMTP).
        .WithEnvironment("GYMHUB_PUBLIC_ORIGIN", publicOrigin)
        .WithEnvironment("GYMHUB_SMTP_HOST", smtpHost)
        .WithEnvironment("GYMHUB_SMTP_PORT", smtpPort)
        .WithEnvironment("GYMHUB_SMTP_USER", smtpUser)
        .WithEnvironment("GYMHUB_SMTP_PASSWORD", smtpPassword)
        .WithEnvironment("GYMHUB_SMTP_FROM", emailFrom)
        .WaitForCompletion(migrations);

    api.WithReference(media)
        .WithEnvironment("Keycloak__Realm", realm)
        // Service discovery gives the API http://keycloak on the environment's private network (Keycloak has no
        // public endpoint of its own); tokens are still checked against the realm's https issuer and signing keys.
        .WithEnvironment("Keycloak__RequireHttpsMetadata", "false")
        .WithEnvironment("Notifications__Smtp__Host", smtpHost)
        .WithEnvironment("Notifications__Smtp__Port", smtpPort)
        .WithEnvironment("Notifications__Smtp__UseTls", "true")
        .WithEnvironment("Notifications__Smtp__UserName", smtpUser)
        .WithEnvironment("Notifications__Smtp__Password", smtpPassword)
        .WithEnvironment("Notifications__Smtp__FromAddress", emailFrom)
        .WithEnvironment("Billing__PublicBaseUrl", publicOrigin)
        .WithEnvironment("Notifications__PublicBaseUrl", publicOrigin)
        .WithEnvironment("Billing__StripeSecretKey", builder.AddParameter("stripe-secret-key", secret: true))
        .WithEnvironment("Billing__StripeWebhookSecret", builder.AddParameter("stripe-webhook-secret", secret: true));

    // Aspire turns on ASP.NET's forwarded headers for published projects, which trusts X-Forwarded-* from any address.
    // ServiceDefaults trusts only ForwardedHeaders:KnownNetworks/KnownProxies (set per environment in milestone 6).
    api.WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "false");
    gateway.WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "false");

    // Encrypts the Gateway's Data Protection key ring in Redis (required outside Development): a base64 PFX.
    var keyRingCertificate = builder.AddParameter("dataprotection-certificate", secret: true);
    var keyRingCertificatePassword = builder.AddParameter("dataprotection-certificate-password", secret: true);

    gateway.WithEnvironment("OpenIdConnect__Authority", ReferenceExpression.Create($"https://{authHost}/realms/{realm}"))
        // The Gateway serves auth-host itself (below), so it reads the metadata and redeems codes on Keycloak's private
        // address; otherwise it would wait on itself while starting. Browsers still go to auth-host.
        .WithEnvironment("OpenIdConnect__MetadataAuthority", $"http://keycloak/realms/{realm}")
        .WithEnvironment("Gateway__AllowedHosts__0", publicHost)
        .WithEnvironment("Gateway__Csp__MediaOrigins__0", mediaOrigin)
        .WithEnvironment("Gateway__DataProtection__Certificate", keyRingCertificate)
        .WithEnvironment("Gateway__DataProtection__CertificatePassword", keyRingCertificatePassword);

    // Keycloak has no public endpoint. The Gateway also answers on auth-host and passes on only what browsers need:
    // the GymHub realm's pages and endpoints, and their static resources. The admin console, the admin REST API and
    // the master realm are never reachable from outside (the API calls the admin REST API on the private network).
    gateway.WithEnvironment("Gateway__AllowedHosts__1", authHost)
        .WithEnvironment("Gateway__AuthHost", authHost)
        .WithEnvironment("ReverseProxy__Routes__keycloak-realm__ClusterId", "keycloak")
        .WithEnvironment("ReverseProxy__Routes__keycloak-realm__Match__Hosts__0", authHost)
        .WithEnvironment("ReverseProxy__Routes__keycloak-realm__Match__Path", $"/realms/{realm}/{{**catch-all}}")
        .WithEnvironment("ReverseProxy__Routes__keycloak-resources__ClusterId", "keycloak")
        .WithEnvironment("ReverseProxy__Routes__keycloak-resources__Match__Hosts__0", authHost)
        .WithEnvironment("ReverseProxy__Routes__keycloak-resources__Match__Path", "/resources/{**catch-all}")
        .WithEnvironment("ReverseProxy__Clusters__keycloak__Destinations__keycloak__Address", "http://keycloak");
}
