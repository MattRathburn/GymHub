# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

GymHub lets users follow coaches and subscribe to their workout plans. It is a .NET Aspire distributed app (`net10.0`, Aspire SDK 13.x) with an Angular 21 frontend, a Blazor admin app, Keycloak for identity, PostgreSQL, and Redis.

**The root `README.md` is stale** — it describes .NET 8, docker-compose, MassTransit, Identity Server, and `src/Presentation`/`src/Infrastructure` projects that do not exist. Trust the code and `GymHub.AppHost/Program.cs` over the README.

## Commands

Run from the repo root unless noted. The solution file is `GymHub.slnx` (the new XML format; requires a recent .NET SDK).

```bash
dotnet build GymHub.slnx
dotnet test GymHub.slnx
dotnet test tests/GH.UnitTests --filter "FullyQualifiedName~ClassName.MethodName"
dotnet run --project src/GymHub.AppHost      # starts everything (needs Docker running)
dotnet tool restore                           # restores Husky.Net
```

Angular app (`src/GH.WebApp/gh.webapp.ng`): `npm install`, `npm start` (ng serve), `npm run build`, `npm test` (Vitest via `ng test`). Prettier config lives in `package.json` (printWidth 100, single quotes).

EF Core migrations — the migrations assembly is `GH.Plan.DbManager` (see `npgsqlOptions.MigrationsAssembly` in its `Program.cs`), while `PlanDbContext` lives in `GH.Plan.Infrastructure`:

```bash
dotnet ef migrations add <Name> --project src/GH.Plan.DbManager --startup-project src/GH.Plan.DbManager
```

`src/GH.Plan.API/Migrations` is an older, separate migration set from before DbManager existed; don't add to it.

Build settings that bite (`Directory.Build.props`): **`TreatWarningsAsErrors` is on**, so any compiler/analyzer warning fails the build. Package versions are centrally managed in `Directory.Packages.props` — add `<PackageReference Include="X" />` without a version in the csproj and put the version in `Directory.Packages.props`.

## Architecture

**Orchestration — `src/GymHub.AppHost/Program.cs`** is the source of truth for what runs and how services find each other. It wires up Keycloak (port 8080, data volume), Postgres `gymhub` with database `plandb` (persistent container + pgAdmin), Redis `plancache`, and the projects `gh-plan-api`, `gh-admin`, `gateway`, plus the Angular app via `AddNpmApp`. Resource names here are what services use for connection strings and service discovery (e.g. `AddNpgsqlDbContext<PlanDbContext>("plandb")`, YARP destination `https://gh-plan-api`). `AddForwardedHeaders()` in `AppHost/Extensions.cs` sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED` on every project.

**Request flow (BFF pattern):** Browser → `Gateway` → backend APIs.
- `src/Gateway` is a YARP-based Backend-for-Frontend. It does the OIDC code+PKCE login against Keycloak, keeps tokens server-side in session (cookie auth to the browser), and on each proxied request `GatewayPipeline` → `GatewayService.AddToken` attaches a bearer token for the target API. Optional token exchange is selected by `Gateway:TokenExchangeStrategy` (`none` | `AzureAd` | `default`) in `GatewaySetup.AddTokenExchangeService`.
- Routes are in `Gateway/appsettings.json` under `ReverseProxy`: `/planapi/**` → Plan API (prefix stripped), `/auth/**` and `/api/**` → Keycloak. The `Apis` section maps API paths to scopes requested for them.
- The gateway enforces XSRF: it issues an `XSRF-RequestToken` cookie and expects an `X-XSRF-TOKEN` header. The Angular `secure-api.interceptor.ts` adds that header for requests to `/api/` and `/planapi/api/` on the current host — new backend prefixes need to be added there too.
- Gateway fetches the OIDC discovery document at startup (`DiscoveryService`), so Keycloak must be up first (AppHost uses `WaitFor(keycloak)`).

**Plan service:**
- `src/GH.Infrastructure` (project/assembly `GH.Plan.Infrastructure`) — `PlanDbContext` and entity models (`GHPlan`, `PlanCreator`, `PlanComponents`, `UserPlanSubscription`, pagination types). Shared by the API, DbManager, and Admin.
- `src/GH.Plan.API` — Minimal APIs with Asp.Versioning. Endpoints are static handler methods in `APIs/PlanAPI.cs`, registered via `app.NewVersionedApi("Plan").MapPlanAPIv1()` under `api/plan`. Dependencies are injected as an `[AsParameters] GHPlanServices` record, and handlers return `TypedResults`/`Results<...>`. User identity comes from the `sub` (fallback `preferred_username`) claim. JWT bearer auth via `AddKeycloakJwtBearer` (audience `plan.api`) is in `Extensions/Extensions.cs`.
- `src/GH.Plan.DbManager` — a run-once worker that creates the database and applies migrations, then stops the host (`PlanDbInitializer`).

**Shared plumbing — `src/GymHub.ServiceDefaults`:** every service calls `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()` (OpenTelemetry, health checks, service discovery, HTTP resilience). Also provides `AddDefaultOpenApi`/`UseDefaultOpenApi` (OpenAPI + Scalar, version-aware), `AddDefaultAuthentication`, and `ClaimsPrincipal` helpers.

**Other projects:**
- `src/GH.Admin` — Blazor Web App (server + WebAssembly `GH.Admin.Client`), still mostly template code.
- `src/EventBus` + `src/EventBusRabbitMQ` — integration-event abstractions and a RabbitMQ implementation (eShop-style). Not yet wired into AppHost or any service.
- `tests/GH.UnitTests`, `tests/GH.FunctionalTests` — xUnit v3; currently placeholders.

## Things to watch

- AppHost runs `gh-plan-dbmanager` first; `gh-plan-api` uses `WaitForCompletion` on it, so a failed migration keeps the API from starting.
- The Keycloak realm is `GH-Test-Realm` everywhere (Gateway authority, Plan API and Admin `AddKeycloakJwtBearer`). Keep them in sync.
- The Plan API has no CORS policy on purpose: the browser reaches it through the Gateway on the same origin.
- The Gateway's `OpenIdConnect:ClientSecret` is blank in `appsettings.json` and comes from the Gateway project's user-secrets. Set it with `dotnet user-secrets set "OpenIdConnect:ClientSecret" <value> --project src/Gateway`.

## Repo tooling

- `.github/instructions/` holds Copilot instructions (general, .NET, Angular), `.github/skills/` and `.github/agents/` hold agent skill/role definitions, and `.github/references/` has security/performance/accessibility/testing checklists — consult these when reviewing or testing.
- `.claude/` contains a multi-phase pipeline (agents, commands, hooks, `rules/review-precedents.md` for recording rejected review findings, `AGENTS.md` for learned patterns).
- Husky.Net git hooks are in `.husky/`; the pre-commit hook currently only echoes and runs no tasks.
