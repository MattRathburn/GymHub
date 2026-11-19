using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.PostgreSql;
using GymHub.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var isPublish = builder.ExecutionContext.IsPublishMode;

// Shared by Keycloak's realm import and the Gateway. Set it in this project's user-secrets.
var bffClientSecret = builder.AddParameter("bff-client-secret", secret: true);

// Shared by the realm import's gymhub-api-admin service account and the API, which grants the trainer role with it.
var apiAdminClientSecret = builder.AddParameter("api-admin-client-secret", secret: true);

// Locally: containers. Published: Azure Database for PostgreSQL and Azure Managed Redis (docs/plans/azure-deployment.md).
IResourceBuilder<AzurePostgresFlexibleServerResource>? azurePostgres = null;
IResourceBuilder<AzureKeyVaultResource>? vault = null;
IResourceBuilder<AzureLogAnalyticsWorkspaceResource>? logs = null;
IResourceBuilder<IResourceWithConnectionString> gymhubDb;
IResourceBuilder<IResourceWithConnectionString> redis;
IResourceBuilder<IResourceWithConnectionString> gatewayRedis;

if (isPublish)
{
    // Every secret lives in Key Vault; Container Apps reads it there with the apps' managed identity.
    vault = builder.AddAzureKeyVault("secrets");

    // The apps run on a subnet of their own; Postgres is reached only through a private endpoint on another
    // (no public access, no firewall rules). The private DNS zone makes the server's usual host name resolve to it.
    // Aspire marks its networking APIs as evaluation-only.
#pragma warning disable ASPIREAZURE003
    var vnet = builder.AddAzureVirtualNetwork("vnet", "10.10.0.0/16");
    var appsSubnet = vnet.AddSubnet("apps", "10.10.0.0/23");
    var privateEndpointsSubnet = vnet.AddSubnet("private-endpoints", "10.10.2.0/27");

    logs = builder.AddAzureLogAnalyticsWorkspace("logs");
    builder.AddAzureContainerAppEnvironment("aca")
        .WithAzureLogAnalyticsWorkspace(logs)
        .WithDelegatedSubnet(appsSubnet)
        // Telemetry goes to Application Insights instead of a dashboard inside the environment.
        .WithDashboard(false);
#pragma warning restore ASPIREAZURE003

    azurePostgres = builder.AddAzurePostgresFlexibleServer("gymhub")
        .WithPasswordAuthentication(vault,
            builder.AddParameter("postgres-user", "gymhub"),
            builder.AddParameter("postgres-password", secret: true))
        .ConfigureInfrastructure(infra =>
        {
            // Point-in-time restore to any moment in the last 14 days (the default is 7).
            var server = infra.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single();
            server.Backup = new PostgreSqlFlexibleServerBackupProperties { BackupRetentionDays = 14 };
        });

    // Also turns off the server's public network access, so Aspire's firewall rule for all of Azure's addresses is gone.
#pragma warning disable ASPIREAZURE003
    privateEndpointsSubnet.AddPrivateEndpoint(azurePostgres);
#pragma warning restore ASPIREAZURE003

    gymhubDb = azurePostgres.AddDatabase("gymhubdb");

    // Lets several API instances work together: the SignalR backplane and instance messages.
    redis = builder.AddAzureManagedRedis("redis").WithAccessKeyAuthentication(vault);

    // The Gateway's own Redis: sessions (users' tokens) and Data Protection keys. Separate from the API's so an API
    // instance never holds credentials to it.
    gatewayRedis = builder.AddAzureManagedRedis("gateway-redis").WithAccessKeyAuthentication(vault);
}
else
{
    var postgres = builder.AddPostgres("gymhub")
        .WithPgAdmin()
        .WithLifetime(ContainerLifetime.Persistent);

    gymhubDb = postgres.AddDatabase("gymhubdb");

    // Lets several API instances work together: the SignalR backplane and instance messages. Without it each instance
    // keeps these to itself.
    redis = builder.AddRedis("redis");

    // The Gateway's own Redis: sessions (users' tokens) and Data Protection keys. Separate from the API's so an API
    // instance never holds credentials to it.
    gatewayRedis = builder.AddRedis("gateway-redis");
}

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true");

var migrations = builder.AddProject<Projects.GymHub_MigrationService>("gymhub-migrations")
    .WithReference(gymhubDb)
    .WaitFor(gymhubDb);

// The API's database connection is set per mode below: locally the server admin's, published its own role's.
var api = builder.AddProject<Projects.GymHub_Api>("gymhub-api")
    .WithReference(keycloak)
    .WithReference(redis)
    .WaitFor(redis)
    .WaitForCompletion(migrations);

var gateway = builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(api)
    .WithReference(keycloak)
    .WithReference(gatewayRedis)
    .WaitFor(gatewayRedis)
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
    keycloak.WithEnvironment("GYMHUB_BFF_CLIENT_SECRET", bffClientSecret)
        .WithEnvironment("GYMHUB_API_ADMIN_CLIENT_SECRET", apiAdminClientSecret);
    api.WithReference(gymhubDb)
        .WithEnvironment("KeycloakAdmin__ClientSecret", apiAdminClientSecret);
    gateway.WithEnvironment("OpenIdConnect__ClientSecret", bffClientSecret);

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

// Production (publish mode): Azure Container Apps, the GymHub realm without test users, Keycloak on Postgres, SendGrid
// SMTP, Cloudflare R2. Every value comes from a parameter (docs/production-dependencies.md); every secret is read
// from Key Vault (docs/plans/azure-deployment.md).
void AddProductionDependencies()
{
    const string realm = "GymHub";

    // A secret parameter stored in Key Vault, referenced by the apps instead of passed as a value.
    IAzureKeyVaultSecretReference Secret(IResourceBuilder<ParameterResource> parameter)
    {
        // The secret keeps the parameter's name in the vault; its Aspire resource needs a name of its own.
        var keyVault = vault!;
        var name = parameter.Resource.Name;
        keyVault.AddSecret($"{name}-secret", name, parameter);
        return keyVault.GetSecret(name);
    }

    var bffSecret = Secret(bffClientSecret);
    var apiAdminSecret = Secret(apiAdminClientSecret);

    keycloak.WithEnvironment("GYMHUB_BFF_CLIENT_SECRET", bffSecret)
        .WithEnvironment("GYMHUB_API_ADMIN_CLIENT_SECRET", apiAdminSecret);
    api.WithEnvironment("KeycloakAdmin__ClientSecret", apiAdminSecret);
    gateway.WithEnvironment("OpenIdConnect__ClientSecret", bffSecret);

    // Host names only, e.g. "gymhub.example" and "auth.gymhub.example"; both are served over https by the ingress.
    var publicHost = builder.AddParameter("public-host");
    var authHost = builder.AddParameter("auth-host");
    var publicOrigin = ReferenceExpression.Create($"https://{publicHost}");
    var authOrigin = ReferenceExpression.Create($"https://{authHost}");

    // The managed certificates' names, empty on the first deployment (docs/runbook.md, "Custom domains").
    var publicHostCertificate = builder.AddParameter("public-host-certificate", "");
    var authHostCertificate = builder.AddParameter("auth-host-certificate", "");

    // SendGrid: the user is literally "apikey" and the password is the API key.
    var smtpHost = builder.AddParameter("smtp-host", "smtp.sendgrid.net");
    var smtpPort = builder.AddParameter("smtp-port", "587");
    var smtpUser = builder.AddParameter("smtp-username", "apikey");
    var smtpPassword = Secret(builder.AddParameter("smtp-password", secret: true));
    var emailFrom = builder.AddParameter("email-from");

    // Cloudflare R2: Endpoint=https://<account>.r2.cloudflarestorage.com;AccessKey=...;SecretKey=...;Bucket=...;Region=auto
    var media = Secret(builder.AddParameter("media", secret: true));
    // The browser loads and uploads media there (presigned URLs): https://<account>.r2.cloudflarestorage.com
    var mediaOrigin = builder.AddParameter("media-origin");

    // Keycloak's own database and role, created by the migration service: Keycloak never gets the server's admin
    // credentials, so it can't touch gymhubdb.
    const string keycloakDatabase = "keycloak";
    var keycloakDbPassword = Secret(builder.AddParameter("keycloak-db-password", secret: true));

    migrations.WithEnvironment("KeycloakDatabase__Name", keycloakDatabase)
        .WithEnvironment("KeycloakDatabase__Password", keycloakDbPassword);

    // The API's own role: read and write on the module schemas, nothing more (the migration job creates it and grants
    // it after every migration). Only the migration job has the server admin's credentials.
    const string appDatabaseUser = "gymhub_app";
    var appDbPassword = Secret(builder.AddParameter("app-db-password", secret: true));

    migrations.WithEnvironment("AppDatabase__User", appDatabaseUser)
        .WithEnvironment("AppDatabase__Password", appDbPassword);

    // The password goes separately (a Key Vault reference can't be part of a longer value); the API joins them.
    api.WithEnvironment("ConnectionStrings__gymhubdb", ReferenceExpression.Create(
            $"Host={azurePostgres!.Resource.HostName};Port=5432;Database=gymhubdb;Username={appDatabaseUser};SSL Mode=Require"))
        .WithEnvironment("Database__Password", appDbPassword);

    // A Container Apps job, started by the pipeline before the apps are updated (docs/runbook.md). Aspire marks the
    // job and custom-domain APIs as evaluation-only (they may change between Aspire versions).
#pragma warning disable ASPIREAZURE002
    migrations.PublishAsAzureContainerAppJob();
#pragma warning restore ASPIREAZURE002

    keycloak.WithDockerfile("./Keycloak")
        .WithEnvironment("KC_DB_URL_HOST", azurePostgres!.Resource.HostName)
        .WithEnvironment("KC_DB_URL_PORT", "5432")
        .WithEnvironment("KC_DB_URL_DATABASE", keycloakDatabase)
        // Azure Database for PostgreSQL only accepts TLS connections.
        .WithEnvironment("KC_DB_URL_PROPERTIES", "?sslmode=require")
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
        .WaitForCompletion(migrations)
        .PublishAsAzureContainerApp((_, app) =>
        {
            // One replica: Keycloak's cluster discovery isn't set up for Container Apps, and it keeps sessions in the
            // database. Its ingress stays internal (no external endpoint); browsers reach it through the Gateway.
            app.Template.Scale = new ContainerAppScale { MinReplicas = 1, MaxReplicas = 1 };
            app.Template.Containers[0].Value!.Resources = new AppContainerResources { Cpu = 1.0, Memory = "2Gi" };
        });

    api.WithEnvironment("ConnectionStrings__media", media)
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
        .WithEnvironment("Billing__StripeSecretKey", Secret(builder.AddParameter("stripe-secret-key", secret: true)))
        .WithEnvironment("Billing__StripeWebhookSecret", Secret(builder.AddParameter("stripe-webhook-secret", secret: true)));

    // Aspire turns on ASP.NET's forwarded headers for published projects, which trusts X-Forwarded-* from any address.
    // ServiceDefaults trusts only ForwardedHeaders:KnownNetworks/KnownProxies instead.
    api.WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "false");
    gateway.WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "false");

    // The ingress and the apps only ever reach these apps from private addresses (nothing on the internet connects to
    // them directly), so the private ranges are the trusted proxies. The API sits behind two: its own ingress and the
    // Gateway, so it walks back two hops to the browser's address. Check it on staging (docs/runbook.md).
    string[] privateNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10"];

    for (var i = 0; i < privateNetworks.Length; i++)
    {
        api.WithEnvironment($"ForwardedHeaders__KnownNetworks__{i}", privateNetworks[i]);
        gateway.WithEnvironment($"ForwardedHeaders__KnownNetworks__{i}", privateNetworks[i]);
    }

    api.WithEnvironment("ForwardedHeaders__ForwardLimit", "2");

    // Logs, traces and metrics from every .NET service (ServiceDefaults exports to it when the connection string is set).
    var insights = builder.AddAzureApplicationInsights("insights").WithLogAnalyticsWorkspace(logs!);

    api.WithReference(insights);
    gateway.WithReference(insights);
    migrations.WithReference(insights);

    // Encrypts the Gateway's Data Protection key ring in Redis (required outside Development): a base64 PFX.
    var keyRingCertificate = Secret(builder.AddParameter("dataprotection-certificate", secret: true));
    var keyRingCertificatePassword = Secret(builder.AddParameter("dataprotection-certificate-password", secret: true));

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

    // Two to five replicas each, and both host names on the Gateway with managed certificates.
    api.PublishAsAzureContainerApp((_, app) => app.Template.Scale = new ContainerAppScale { MinReplicas = 2, MaxReplicas = 5 });

    gateway.PublishAsAzureContainerApp((_, app) =>
    {
        app.Template.Scale = new ContainerAppScale { MinReplicas = 2, MaxReplicas = 5 };
#pragma warning disable ASPIREACADOMAINS001
        app.ConfigureCustomDomain(publicHost, publicHostCertificate);
        app.ConfigureCustomDomain(authHost, authHostCertificate);
#pragma warning restore ASPIREACADOMAINS001
    });
}
