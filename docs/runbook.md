# Runbook: GymHub on Azure

How GymHub runs on Azure Container Apps (East US 2), and how to set it up, deploy, roll back, restore and rotate secrets. The AppHost (`src/GymHub.AppHost/Program.cs`) describes every resource; `docs/plans/azure-deployment.md` explains the design, and `docs/production-dependencies.md` covers SendGrid, R2 and Keycloak.

## What runs where

| Piece | Azure resource | Notes |
|---|---|---|
| Gateway | Container App `gateway`, external, 2 to 5 replicas | Serves `public-host` and `auth-host` with managed certificates |
| API | Container App `gymhub-api`, internal, 2 to 5 replicas | |
| Keycloak | Container App `keycloak`, internal, 1 replica, 1 CPU and 2 GiB | Built from `src/GymHub.AppHost/Keycloak/Dockerfile`. Reached only through the Gateway, never `/admin` |
| Migrations | Container Apps job `gymhub-migrations` | Started by the pipeline before each app update |
| Database | PostgreSQL Flexible Server `gymhub` (Burstable B1ms, 32 GB, 14 days of point-in-time restore) | Holds `gymhubdb` and `keycloak` |
| Network | Virtual network `vnet` (10.10.0.0/16): subnet `apps` (/23) for the Container Apps environment, `private-endpoints` (/27) for Postgres's private endpoint, private DNS zone `privatelink.postgres.database.azure.com` | Set when the environment is created; Container Apps can't move an existing environment onto a VNet |
| Redis | Azure Managed Redis `redis` and `gateway-redis` (Balanced B0) | |
| Secrets | Key Vault `secrets` | Every secret parameter, plus the database and Redis connection strings |
| Telemetry | Application Insights `insights` and Log Analytics `logs` | |
| Images | Azure Container Registry (Basic) | |

**Costs:** the main costs are the always-on replicas (2 Gateway, 2 API, 1 Keycloak), two Managed Redis instances and the Postgres server. Postgres's private endpoint (billed hourly and per GB) and the private DNS zone add a little; the VNet itself is free. Check the Azure pricing calculator for current prices before the first `azd up`, and set a budget alert on the subscription.

**Database network access:** Postgres has public network access disabled and no firewall rules. The apps and the migration job reach it through a private endpoint on the environment's VNet; the private DNS zone makes the server's usual host name (`<server>.postgres.database.azure.com`) resolve to the endpoint's private address, so connection strings don't change. Nothing outside the VNet can connect, your machine and the GitHub runner included: run database work from inside the VNet (see "Restoring the database").

## First-time setup (per environment)

Do staging first, then production the same way, with different names and values.

1. **Tools:** install the [Azure Developer CLI](https://aka.ms/azd-install) and the [Azure CLI](https://aka.ms/installazurecli), then run `az login` and `azd auth login`.
2. **Outside Azure:** set up the SendGrid account, the R2 bucket (with CORS for this environment's origin) and the domain, as described in `docs/production-dependencies.md`. Staging needs its own host names, for example `staging.gymhub.example` and `auth.staging.gymhub.example`.
3. **Create the azd environment:**
   ```bash
   azd env new gymhub-staging
   azd env set AZURE_LOCATION eastus2
   ```
4. **Generate the secrets.** Every secret parameter is listed in `docs/production-dependencies.md`, plus these:
   - `postgres-user`, which defaults to `gymhub`.
   - `postgres-password`, `keycloak-db-password` and `app-db-password`: `openssl rand -base64 32` each.
   - `bff-client-secret` and `api-admin-client-secret`: `openssl rand -hex 32` each.
   - `dataprotection-certificate` and its password: a certificate that encrypts the Gateway's key ring.
     ```bash
     openssl req -x509 -newkey rsa:3072 -sha256 -days 1825 -nodes -subj "/CN=gymhub-dataprotection" -keyout dp.key -out dp.crt
     openssl pkcs12 -export -inkey dp.key -in dp.crt -out dp.pfx -passout pass:<certificate-password>
     base64 -w0 dp.pfx   # the dataprotection-certificate value
     ```
     Keep the PFX somewhere safe (a password manager), then delete the local files.
   - `public-host-certificate` and `auth-host-certificate`: leave these empty for now (see "Custom domains").
5. **Provision:** run `azd provision`. It asks for each parameter and stores the answers in `.azure/gymhub-staging/config.json`. That folder is git-ignored; never commit it.
6. **Migrations, then the apps:**
   ```bash
   azd deploy gymhub-migrations
   group=$(azd env get-value AZURE_RESOURCE_GROUP)
   az containerapp job start --name gymhub-migrations --resource-group "$group"
   az containerapp job execution list --name gymhub-migrations --resource-group "$group" --output table   # wait for Succeeded
   azd deploy
   ```
7. **Custom domains:** see below.
8. **First admin:** follow `docs/production-dependencies.md`. Open the shell with `az containerapp exec --name keycloak --resource-group "$group" --command bash`.
9. **Check the client IP:** see below.

### Custom domains

Container Apps needs DNS to point at the app before it issues a certificate, so this takes two passes.

1. Get the Gateway's address and the environment's verification ID:
   ```bash
   az containerapp show --name gateway --resource-group "$group" --query properties.configuration.ingress.fqdn --output tsv
   az containerapp env show --name <environment> --resource-group "$group" --query properties.customDomainConfiguration.customDomainVerificationId --output tsv
   ```
   To find `<environment>`, run `az containerapp env list -g "$group" -o table`.
2. At your DNS provider, add these for each host (`public-host` and `auth-host`): a `CNAME` from the host to the Gateway's address, and a `TXT` record `asuid.<host>` with the verification ID.
3. Bind each host with a managed certificate:
   ```bash
   az containerapp hostname bind --name gateway --resource-group "$group" --environment <environment> --hostname <host> --validation-method CNAME
   ```
4. Find the certificates' names with `az containerapp env certificate list --name <environment> --resource-group "$group" --managed-certificates-only --output table`. Put them in `.azure/<env>/config.json` under `infra.parameters`, as `public-host-certificate` and `auth-host-certificate`, so later deployments keep the bindings. Then run `azd provision`.

### Checking the client IP

The apps trust `X-Forwarded-For` only from private addresses, and the API walks back two hops: its own ingress, then the Gateway. If that's right, the API sees browsers' public addresses, which its per-IP rate limit depends on.

After deploying, browse the site, then run this in Application Insights → Logs:

```kusto
requests
| where cloud_RoleName == "gymhub-api" and timestamp > ago(1h)
| summarize count() by client_IP
```

Public addresses mean it works. Addresses in 10.x, 100.64–127.x or 172.16–31.x mean a hop is missing or extra: change `ForwardedHeaders__ForwardLimit` for the API in the AppHost.

## Pipeline

`.github/workflows/deploy.yml` runs on every push to `main`. It deploys to staging first, then to production after a reviewer approves the `production` GitHub environment. Each deployment provisions, runs the migration job, then deploys the apps (`deploy-environment.yml`). It does nothing until you set it up:

1. **GitHub environments:** create `staging` and `production` (Settings → Environments). Give `production` required reviewers.
2. **Azure sign-in:** from your machine, with the staging azd environment selected, run `azd pipeline config --provider github --auth-type federated`. It creates an app registration that GitHub signs in as, and sets repository variables and secrets.
   - The jobs run inside GitHub environments, so the OIDC subject is `repo:<owner>/<repo>:environment:<name>`. Add one federated credential per environment:
     ```bash
     az ad app federated-credential create --id <app-id> --parameters '{"name":"github-staging","issuer":"https://token.actions.githubusercontent.com","subject":"repo:<owner>/<repo>:environment:staging","audiences":["api://AzureADTokenExchange"]}'
     az ad app federated-credential create --id <app-id> --parameters '{"name":"github-production","issuer":"https://token.actions.githubusercontent.com","subject":"repo:<owner>/<repo>:environment:production","audiences":["api://AzureADTokenExchange"]}'
     ```
   - Give the app registration the same roles on production's subscription or resource group that `azd pipeline config` gave it for staging. Those are Contributor and User Access Administrator, because the deployment assigns roles to the apps' identities.
3. **Per environment:** in each GitHub environment, set:
   - the variables `AZURE_ENV_NAME` (`gymhub-staging` or `gymhub-prod`), `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`
   - the secret `AZD_INITIAL_ENVIRONMENT_CONFIG`, set to the contents of `.azure/<env>/config.json`, which carries the parameter values
   Then delete the repository-level copies that `azd pipeline config` created, so each environment only uses its own.
4. **Switch it on:** set the repository variable `DEPLOY_ENABLED` to `true`.

## Deploying
- **Normally:** merge to `main`. The staging deployment runs on its own; check staging, then approve production in the workflow run.
- **By hand:** select the environment (`azd env select gymhub-staging`) and repeat step 6 of the first-time setup.

## Rolling back
- **Apps:** Container Apps keeps earlier revisions. To send traffic back to the previous one:
  ```bash
  az containerapp revision list --name gymhub-api --resource-group "$group" --output table
  az containerapp ingress traffic set --name gymhub-api --resource-group "$group" --revision-weight <previous-revision>=100
  ```
  Do the same for `gateway` and `keycloak` if they changed. Alternatively, run the Deploy workflow by hand (Run workflow) from an earlier commit or tag.
- **Migrations are forward-only:** there are no down-migrations. Code you roll back to must still work with the newer schema, so keep migrations additive, and remove columns in a later release than the one that stops using them.

## Restoring the database
Point-in-time restore creates a **new** server; the original keeps running.

```bash
az postgres flexible-server restore --resource-group "$group" --name gymhub-restore-<date> \
  --source-server <server-name> --restore-time "2026-11-18T09:30:00Z"
```

The restored server has public network access disabled like the original, but no private endpoint, so nothing can reach it yet. Give it one in the `private-endpoints` subnet, using the same private DNS zone:

```bash
az network private-endpoint create --resource-group "$group" --name gymhub-restore-pe   --vnet-name <vnet-name> --subnet private-endpoints --connection-name gymhub-restore   --private-connection-resource-id $(az postgres flexible-server show -g "$group" -n gymhub-restore-<date> --query id -o tsv)   --group-id postgresqlServer
az network private-endpoint dns-zone-group create --resource-group "$group" --endpoint-name gymhub-restore-pe   --name default --private-dns-zone privatelink.postgres.database.azure.com --zone-name postgres
```

`psql`, `pg_dump` and `pg_restore` have to run on the VNet too, for example from a temporary Container Apps job in the environment using the `postgres:16` image.

1. Check the restored data on the new server.
2. In a maintenance window, put the apps down to zero replicas (or stop traffic at the Gateway).
3. Copy the data back with `pg_dump`/`pg_restore` (`gymhubdb`, and `keycloak` if needed).
4. Bring the apps back up, and delete the restore server and its private endpoint.

This keeps every connection string and secret as it is.

## Rotating secrets
1. **Change the value:** edit it in `.azure/<env>/config.json` (and in that GitHub environment's `AZD_INITIAL_ENVIRONMENT_CONFIG`). Run `azd provision`, which writes the new value to Key Vault.
2. **Restart the apps** that read it, so they pick it up straight away (Container Apps otherwise refreshes Key Vault references within about 30 minutes):
   ```bash
   az containerapp revision restart --name gymhub-api --resource-group "$group" --revision <active-revision>
   ```

Some secrets need more than that:

- **`bff-client-secret` and `api-admin-client-secret`:** Keycloak imported the realm only once, so also change the client's secret in Keycloak, with `kcadm.sh update clients/<id> -r GymHub -s secret=<new>`. Then restart Keycloak, the Gateway and the API.
- **`keycloak-db-password`:** the migration job applies the new password to Keycloak's role. Run the job, then restart Keycloak.
- **`postgres-password`:** `azd provision` sets the new password on the server. Run the migration job (the only thing that uses it).
- **`app-db-password`:** the migration job applies the new password to the API's `gymhub_app` role. Run the job, then restart the API.
- **`dataprotection-certificate`:** the existing key ring was encrypted with the old certificate, so everyone is signed out. Rotate it when that's acceptable.
- **`smtp-password`, `media`, `stripe-*`:** restart the API. The SMTP password is also in Keycloak's realm. Update it in the admin console with `kcadm.sh update realms/GymHub -s 'smtpServer.password=<new>'`, then restart Keycloak.
