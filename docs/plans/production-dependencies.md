# Milestone 5: Production dependencies

Prepares the AppHost's publish mode for production: Keycloak, email, media storage and secrets. Running locally (`dotnet run` on the AppHost) doesn't change. Milestone 6 deploys what this describes to Azure.

## Decisions
- **Email:** SendGrid over SMTP (`smtp.sendgrid.net:587`, STARTTLS, user `apikey`, the API key as password), for GymHub's notifications and Keycloak's verification and password-reset emails. Mailpit stays for local runs. Emails to unverified addresses are local only.
- **Media:** Cloudflare R2, which is S3-compatible, so the storage code stays and only configuration changes. CORS is set on the R2 bucket.
- **Keycloak database:** a `keycloak` database on the same Postgres server as `gymhubdb`.
- **First admin:** no seeded users in production. You sign up normally, then a one-off `kcadm` command grants your account the `admin` role (see `docs/production-dependencies.md`).

## Design
- **Publish-mode AppHost** (`ExecutionContext.IsPublishMode`):
  - Keycloak is built from `Keycloak/Dockerfile`: Keycloak 26.5 with the production realm copied into its import folder (Container Apps has no bind mounts) and Postgres as its database. It runs in production mode (`start`) behind the ingress (`KC_PROXY_HEADERS=xforwarded`, http inside), with `KC_HOSTNAME` from the `auth-origin` parameter and no data volume.
  - Media comes from a `media` connection string parameter (R2), not the RustFS container.
  - SMTP settings are parameters for both the API and Keycloak, with TLS on.
  - Stripe keys are always parameters (when running locally they stay optional).
  - The site's address is the `public-origin` parameter. It feeds the realm's redirect URIs and web origin, the links in emails and Stripe return URLs, and the Gateway's allowed host. The Gateway's OIDC authority is `auth-origin` + `/realms/GymHub`, and its CSP media origin is the `media-origin` parameter.
- **Production realm** (`Keycloak/realm/GymHub-realm.json`, realm `GymHub`): the same roles, clients and mappers as the test realm, and:
  - no users except the API's service account
  - `sslRequired: external`, and redirect URIs only under `public-origin`
  - brute-force protection and a password policy
  - email verification, and SMTP with sign-in and STARTTLS
  - A unit test keeps it in step with the test realm and checks those rules.
- **Storage configuration:** the `media` connection string can set `Bucket` (default `gymhub-media`) and `Region` (default `us-east-1`; `auto` for R2). At startup the bucket is checked with HeadBucket and created only if it's missing, so a bucket-scoped R2 token works (it can't list buckets).
- **SMTP:** refuses to start with a username but without TLS, so credentials never cross the network in clear.
- **Secrets:** every production secret is an Aspire parameter (list in `docs/production-dependencies.md`). Milestone 6 stores them in Key Vault.

## As built
- Everything above. The setup steps and the parameter list are in `docs/production-dependencies.md`.
- **Forwarded headers:** the publish manifest showed that Aspire sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on published projects. That makes ASP.NET clear the trusted proxy lists and trust `X-Forwarded-*` from anyone, undoing milestone 3. The AppHost now sets it to `false` for the API and Gateway, and `UseDefaultForwardedHeaders` refuses to start if it's on (a Gateway test covers this).
- **Published manifest:** only production resources. Mailpit, RustFS, pgAdmin and the dev server don't appear, Keycloak has a Dockerfile build and no bind mounts or volumes, and every secret is a parameter.
- **Tests:**
  - `ProductionRealmTests` (5): no seeded users, the production settings, redirect URIs only under the public origin, secrets from the environment, and the same roles, clients, mappers and service-account roles as the test realm.
  - A functional test: SMTP credentials without TLS stop the API from starting.
  - A Gateway test for the forwarded-headers guard.
- **Checked by hand:** I built the image and ran it in production mode against a scratch Postgres.
  - The `GymHub` realm imported and the placeholders were filled from the environment (redirect URIs and SMTP).
  - The settings were right: `sslRequired`, email verification, brute-force protection and the password policy.
  - The service account has `manage-users` and `view-realm`, and PKCE is required.
  - A redirect to another site is refused, and the sign-up form loads.
  - The documented `kcadm add-roles` command granted `admin` to a signed-up user.
  - The local AppHost still runs (all healthy, with the test realm).
- **Not exercised:** a real SendGrid send and a real R2 bucket, because neither account exists yet. Nor was the "create a missing bucket" branch run; locally the bucket already exists. These should be checked on staging in milestone 6.
- **For milestone 6:** Postgres moves to Azure Flexible Server (Keycloak's `KC_DB_*` settings follow its endpoint), the parameters go into Key Vault, `ForwardedHeaders:KnownNetworks` is set for the ingress.

## After review
- **Keycloak behind the Gateway:** Keycloak has no public endpoint. The Gateway answers on `auth-host` and passes on only `/realms/GymHub/**` and `/resources/**`, so the admin console, the admin REST API and the `master` realm aren't reachable from outside. The Gateway adds no CSP there (Keycloak sends its own), and other paths on `auth-host` are 404.
- **Private metadata:** the Gateway reads the realm's metadata and redeems codes at `http://keycloak/realms/GymHub` (`OpenIdConnect:MetadataAuthority`); otherwise it would wait on itself while starting, since it now serves `auth-host`. The API reads it there too (`Keycloak:RequireHttpsMetadata=false`). Tokens are still checked against the https issuer.
- **Keycloak's database role:** the migration service creates a `keycloak` role (password `keycloak-db-password`) that owns only the `keycloak` database, and revokes everyone's default CONNECT on `gymhubdb`. Keycloak waits for it and never gets the server admin's credentials.
- **Gateway key ring:** the published Gateway gets `dataprotection-certificate` and its password.
