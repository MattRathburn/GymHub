# Milestone 6: Azure deployment

Puts GymHub on Azure Container Apps in East US 2, with a staging and a production environment and a GitHub Actions pipeline. This milestone is code and documentation only: you run the first deployment (`docs/runbook.md`), so nothing here creates or bills Azure resources.

## Decisions
- **Scope:** the AppHost describes the Azure resources, `azure.yaml` lets `azd` deploy them, and a workflow deploys `main`. The first `azd up` is yours.
- **Region:** East US 2, for both environments.
- **Keycloak:** reached only through the Gateway, which serves `auth-host` and passes on only the GymHub realm's pages and endpoints (built before this milestone). Keycloak's own ingress is internal.
- **Bicep:** generated from the AppHost on every deploy, not committed. The AppHost is the one description of the infrastructure; a committed copy that `azd` doesn't read would drift. `aspire publish` writes it out for review (see the runbook).

## Design
- **Container Apps environment** (`AddAzureContainerAppEnvironment`), with its registry and Log Analytics workspace. The Aspire dashboard is off; telemetry goes to Application Insights.
- **Postgres:** Azure Database for PostgreSQL Flexible Server with password sign-in, and its connection string kept in Key Vault. It holds `gymhubdb` and `keycloak`, with 14 days of point-in-time restore. Local runs keep the existing Postgres container (`IsPublishMode` picks the resource).
- **Redis:** two Azure Managed Redis instances (the API's and the Gateway's, kept apart as before), with access keys in Key Vault. Local runs keep the containers.
- **Key Vault:** holds every secret parameter (`AddSecret`), plus the database and Redis connection strings. Container Apps reads them through managed identity. The values themselves are azd environment parameters, set once per environment.
- **Application Insights:** the API, Gateway and migration job export logs, traces and metrics with the Azure Monitor exporter when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
- **Apps:**
  - The API and Gateway run 2 to 5 replicas.
  - Keycloak runs one replica with 1 CPU and 2 GiB, on internal ingress only.
  - The migration service is a Container Apps **job**, started by the pipeline before the apps are updated, so the schema and Keycloak's database role exist first.
- **Custom domains:** `public-host` and `auth-host` are bound to the Gateway with managed certificates. As Container Apps requires, the first deployment binds them without a certificate, then each certificate's name is set (runbook).
- **Forwarded headers:** the ingress and the apps use private addresses, so `ForwardedHeaders:KnownNetworks` lists the private ranges. The API sees two proxies (its ingress, and the Gateway behind its own ingress), so it takes `ForwardedHeaders:ForwardLimit` = 2, to see the browser's IP rather than the Gateway's.
- **Pipeline** (`.github/workflows/deploy.yml`): signs in to Azure with OIDC federated credentials (`azd pipeline config`, no stored Azure secret). Every deploy of `main` goes to staging. Production is the same job behind a GitHub environment that requires approval. Each deploy: provision, run the migration job, then deploy the apps. It stays off until the repository variable `DEPLOY_ENABLED` is `true`.
- **Runbook** (`docs/runbook.md`): first-time setup, deploy, roll back, restore the database, rotate secrets, and check the client IP.

## As built
- Everything above. `aspire publish` produces the Bicep, which I checked:
  - The migration service is a manually triggered job.
  - Keycloak's ingress is internal, with one replica, 1 CPU and 2 GiB.
  - The API is internal and the Gateway external, each with 2 to 5 replicas.
  - Both host names are bound to the Gateway, getting a certificate once one's name is set.
  - Postgres keeps 14 days of backups.
  - Every app secret is a Key Vault reference, and every top-level secret parameter is `@secure()`.
- **Key Vault secrets:** `AddSecret` needs a resource name of its own (`<parameter>-secret`); the vault secret keeps the parameter's name.
- **Evaluation-only APIs:** Aspire marks `PublishAsAzureContainerAppJob` and `ConfigureCustomDomain` as evaluation-only (`ASPIREAZURE002`, `ASPIREACADOMAINS001`). They're suppressed at the call sites, and an Aspire upgrade may change them.
- **Not in Key Vault:** `WithReference` also hands the API and migration job the database password and URI as Container Apps secrets (encrypted, but outside Key Vault). Keycloak's bootstrap password, which azd generates, is a Container Apps secret too.
- **Postgres:** version 16 on Burstable B1ms. Aspire's default firewall rule admitted Azure's address ranges, still behind the password; closed after review (below).
- **Local runs:** `IsPublishMode` picks the resources, so local runs keep the same containers. The persistent Postgres container keeps its name and data. Checked: every service reports Healthy and sign-in still redirects to the test realm.
- **Tests:**
  - `ForwardedHeadersTests` (2): the API's two-hop walk reaches the browser's address, the default single hop would stop at the Gateway, and a forged private address is ignored.
  - All 147 unit, 28 Gateway and 110 functional tests pass.
- **`.azure/`** (azd's parameter values, secrets included) is now git-ignored.
- **Not exercised:** nothing has been deployed. Provisioning, the pipeline, custom domains, Key Vault references at run time and the client-IP check all wait for the first `azd up` (`docs/runbook.md`). The workflows and `azure.yaml` parse, but they haven't run.

## After review
- **Database roles on Azure:** the migration job's role setup now works for Azure's admin, which isn't a superuser. It makes itself a member of each role before handing it a database (Postgres 16 requires that), and only changes who may connect to `gymhubdb` if it owns that database. `KeycloakDatabaseTests` runs it as such an admin (only CREATEROLE and CREATEDB, in a database a superuser created), and fails with Azure's "must be able to SET ROLE" error without the fix.
- **The API's own role:** the API no longer has the server admin's credentials. It connects as `gymhub_app` (password `app-db-password` in Key Vault, passed as `Database:Password` and joined to the connection string at startup), which can only read and write the modules' tables: no DDL, no Keycloak database. The migration job creates the role and grants it every module schema after each migration.
- **Private network for Postgres:** the Container Apps environment runs on subnet `apps` (10.10.0.0/23, delegated to `Microsoft.App/environments`) of a VNet (`Aspire.Hosting.Azure.Network`), and Postgres is reached through a private endpoint on subnet `private-endpoints` (10.10.2.0/27) with the private DNS zone `privatelink.postgres.database.azure.com` linked to the VNet. Adding the endpoint sets the server's public network access to `Disabled`, so Aspire's firewall rule for Azure's address ranges is gone. The server keeps its public host name, which resolves privately inside the VNet, so no connection string changed. The generated Bicep has no firewall rule, `publicNetworkAccess: 'Disabled'`, `vnetConfiguration.infrastructureSubnetId` on the environment, and the endpoint's DNS zone group in that zone. Aspire marks these networking APIs evaluation-only (`ASPIREAZURE003`, suppressed at the call sites). It has to be in place before the first `azd up`: an existing environment can't be moved onto a VNet. Database work from outside Azure (a restore, `psql`) now runs from inside the VNet (runbook).
- **Still open:** Key Vault and the two Managed Redis instances still have public endpoints (behind managed identity and access keys). Private endpoints for them would work the same way.
