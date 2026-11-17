# Roadmap: GymHub to a finished MVP

## Context
The core modules are built: Programs, Subscriptions, Trainers, Discussions, Billing, Notifications and Training Log (Training Log is still uncommitted on `training-log`). Two surveys of the code found what an MVP still needs, and you set the scope:
- **Hosting:** Azure Container Apps.
- **Product:** Reviews, better discovery, account and legal basics, and admin moderation.
- **Scale:** must run on **multiple instances**.
- **Payments:** **live Stripe** at launch.

The roadmap is a series of milestones, each on its own branch with its own PR. Features that need design choices get their own plan in `docs/plans/` when they start, the way Training Log did. This file is the order and the outline.

## Milestone 0: Close out Training Log
- Run the secret scan, commit `training-log`, open the PR and merge it.

## Milestone 1: CI and repo cleanup (small, first, so later PRs get checked)
- Add `.github/workflows/ci.yml`. On PRs and pushes to `main` it runs:
  - `dotnet build GymHub.slnx` and `dotnet test GymHub.slnx` (ubuntu runners have Docker for Testcontainers)
  - `npm ci`, `npm test` and `npm run build` in `src/GH.WebApp/gh.webapp.ng`
  - a secret scan (gitleaks), so the "no private keys in tracked files" rule is enforced
- Make `main` a protected branch that requires the CI check.
- **Cleanup:**
  - Remove leftovers: `EventBusRabbitMQ` (unused; `EventBus` stays, the outbox uses it), the `GH.Admin` and `GH.Plan.*` folders, the unused Redis `plancache` resource and unused AppHost packages. Milestone 4 brings Redis back for a real purpose.
  - Fix `README.md`: the module list, the `api-admin-client-secret` secret, and the dead Contributing and LICENSE links.

## Milestone 2: Product features
Each is its own PR, built in this order. Discovery uses ratings from Reviews.

### 2a. Reviews module (new module, own plan)
- `Review`: one per member per program, 1–5 stars plus optional text, editable.
- **Who can review:** members with `IEntitlements` access, after a minimum enrollment period.
- **Trainer reply:** the trainer can reply once.
- **Moderation:** reviews can be reported and hidden, reusing the Discussions report pattern.
- **Ratings in other modules:** a `.Contracts` interface `IProgramRatings` (average and count), read by the Programs catalog.
- **Notifications:** a new-review notification for the trainer, handled in Notifications.

### 2b. Discovery
- **Catalog** (`Programs/Features/BrowsePrograms/BrowsePrograms.cs`, `program-list.component.ts`):
  - search the summary and trainer name, not just the title
  - sorting: newest, rating, price
  - filters: kind and price range
  - real paging in the UI
- **Trainer directory:** a new `ListTrainers` slice in Trainers, and a `/trainers` page.
- **Landing page at `/`:** a hero, featured and top-rated programs, and how the site works. The catalog moves to `/programs`.

### 2c. Account and legal basics
- **Account deletion:**
  - Each module handles a `UserDeletionRequested` integration event idempotently: it anonymizes or deletes its data. Training logs are deleted; discussion posts become "deleted user"; purchases are kept for accounting with the person's details removed.
  - Deletion is blocked while a paid subscription is active, until it's cancelled.
  - The Keycloak user is deleted with the admin client.
- **Data export:** a JSON download built from each module's contract.
- **Pages:** terms, privacy policy, a footer, a real 404 page (instead of `**` redirecting to `/`), a "Sign up" button (Keycloak registration is already on), (Leaving a free program already exists: `DELETE /api/v1/subscriptions/programs/{id}` and the "Leave program" button.)
- **Sign-up checks:** turn on Keycloak email verification and password reset (needs the production SMTP from Milestone 5).

### 2d. Admin moderation
- **Programs:**
  - an admin program list
  - archive or unpublish any program, from that list and from the program page (the backend already lets admins archive: `ArchiveProgram` checks `IsOwnedBy || user.IsAdmin`)
- **Users:**
  - suspend or reactivate a user (disable them in Keycloak and end their sessions)
  - revoke the trainer role
- **Dashboard:** a simple metrics page with users, trainers, published programs, enrollments, revenue and open reports.

## Milestone 3: Security and production hardening
- **Gateway serves the built SPA:**
  - In production, YARP's `spa` route is replaced by static files from the Angular `dist`, using `UseStaticFiles` plus `MapFallbackToFile("index.html")`. The Gateway image builds the Angular app in a multi-stage Dockerfile.
  - `ng serve` stays for development only.
  - This also removes the need for a separate web app container (`AddNpmApp ... PublishAsDockerFile` has no Dockerfile today).
- **Configuration:**
  - The Keycloak realm name and authority move to configuration (they are hardcoded in `GymHub.Api/Program.cs:26` and `Gateway/appsettings.json`).
  - `RequireHttpsMetadata` is on outside Development.
  - `AllowedHosts` is set.
  - Forwarded headers trust only the ingress.
- **Headers and limits:**
  - security headers on the Gateway: CSP, HSTS, `X-Content-Type-Options`, `Referrer-Policy` and frame-ancestors
  - ASP.NET rate limiting on the API and the login endpoints
- **Uploads:**
  - Uploads get a size limit and an exact content-type allowlist.
  - The server checks the object (HEAD: size and type) before attaching it (`BuildingBlocks/Storage/ObjectStorage.cs`, `ManageMedia.cs`, `EditMyProfile.cs`).
- **Health and startup:**
  - `/health` and `/alive` are mapped in all environments, with checks for Postgres, Keycloak and storage (today they're Development-only, in `ServiceDefaults/Extensions.cs:114`).
  - The Gateway retries the Keycloak discovery at startup.
- **API docs:** OpenAPI and Scalar are off in production.

## Milestone 4: Multi-instance readiness
- **Outbox:** `OutboxDispatcher` claims a batch with `FOR UPDATE SKIP LOCKED` (and a lock timeout), so two instances never dispatch the same message. Handlers are already idempotent, which still matters for retries.
- **Singleton jobs:** a small `ISingletonJobLock` in BuildingBlocks, using a Postgres advisory lock. `WeekReleaseJob`, `NotificationCleanupJob` and `EmailDispatcher` run on one instance at a time. `EmailDispatcher` could claim rows with `SKIP LOCKED` instead.
- **SignalR:** Azure SignalR Service, or the Redis backplane with the Redis from Milestone 6. The recommendation is the Redis backplane, which is one dependency, cheaper and the same code in development.
- **Gateway:**
  - Sessions move to Redis (`AddStackExchangeRedisCache` in place of `AddDistributedMemoryCache`).
  - Data Protection keys are persisted to Redis or Blob and protected with Key Vault.
- **Tests:** two `ApiFactory` instances on one database dispatch an outbox batch exactly once, and only one job lock is held at a time.

## Milestone 5: Production dependencies
- **Keycloak:**
  - production mode on its own Postgres database
  - a real hostname, `sslRequired: external`, email verification on and SMTP configured
  - a **production realm file without the seeded test users** (`GH-Test-Realm` stays for development)
  - the first admin is created by a one-off step, not seeded
- **Email:** Azure Communication Services Email (or SendGrid) over SMTP with authentication and TLS. Mailpit stays for development, and `EmailUnverifiedAddresses` is off in production.
- **Media storage:**
  - `IObjectStorage` is S3-only, and Azure Blob isn't S3-compatible.
  - The recommendation is **Cloudflare R2**: S3-compatible, so no code changes, and no egress fees.
  - Writing a Blob implementation is the all-Azure alternative.
  - CORS is set to the production origin; it is hardcoded to localhost in AppHost L98-99 today.
- **Secrets:** everything moves to Key Vault, read as Aspire parameters at deploy time.

## Milestone 6: Azure deployment
- `azd init` from the AppHost, with Bicep committed under `infra/`. The deployment uses:
  - Container Apps for the API, Gateway, MigrationService (a job) and Keycloak
  - Azure Database for PostgreSQL Flexible Server, with point-in-time restore backups
  - Azure Cache for Redis
  - Key Vault, and Application Insights through the existing OTLP export
- **Environments:** a staging and a production environment, a custom domain and a managed TLS certificate, and at least 2 API and 2 Gateway replicas.
- **CD:** a GitHub Actions workflow (OIDC federated credentials, no stored Azure secrets) deploys `main` to staging and promotes to production by hand.
- **Runbook** in `docs/`: deploy, roll back, restore the database, rotate secrets.

## Milestone 7: Stripe to live
1. **Test mode, end to end on staging** (this has never been run):
   - Connect onboarding
   - a one-time and a subscription checkout
   - renewal, a price change and the move to free
   - refunds and a dispute
   - the webhook going through the Gateway `/stripe/webhook`
2. **Go live:**
   - complete the Stripe platform profile and Connect settings
   - create the live webhook endpoint at the production domain
   - put the live `sk_live_`/`whsec_` keys in Key Vault only
   - set the final `Billing:PlatformFeePercent`
3. **Payment tests:** one live purchase and refund with a real card.

## Milestone 8: Launch checklist
- Write the final terms and privacy text. That content is yours; the pages come from 2c.
- Alerts in Application Insights: 5xx rate, failed webhooks, a stuck outbox (age of the oldest undispatched message) and failed emails.
- A smoke test against production: sign up, verify email, browse, enroll, buy, post, log a workout, review.
- Create the first admin and approve the first trainers.

## Suggested order and parallelism
- 0 → 1 first.
- Then 2a–2d (product) and 3–4 (hardening) can go in parallel; they touch mostly different files.
- 5 and 6 need 3 and 4.
- 7 needs 6 (staging).
- 8 is last.

## Verification (every milestone)
- CI green: `dotnet build`/`test` and Angular `test`/`build`, with new unit and functional tests per feature.
- An end-to-end pass in the AppHost through the Gateway at https://localhost:7179, as `member`, `trainer` and `admin`, like the Training Log run.
- From Milestone 6 on: the same pass on staging before promoting.
- The secret scan before every commit.
