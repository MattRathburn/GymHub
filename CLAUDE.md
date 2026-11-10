# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

GymHub is a workout-program platform: trainers (approved by an admin) publish programs, users subscribe or buy them, and each program gets a subscriber-only discussion board. It is a **modular monolith with vertical slices** on .NET 10 + Aspire, with a YARP **BFF** (`Gateway`), **Keycloak** auth, PostgreSQL, S3-compatible media storage, and an Angular 21 frontend.

Built so far: the foundation and the **Programs**, **Trainers** (applications, profiles, follows, member profiles) and **Discussions** modules, plus a minimal **Subscriptions** module (free enrollment only). Planned modules, each with its own plan: Billing (Stripe Connect), Training Log, Notifications, Reviews. Plans live in `docs/plans/`.

The root `README.md` is partly stale; trust the code, `GymHub.AppHost/Program.cs` and `.github/instructions/` over it.

## Commands

Run from the repo root. The solution file is `GymHub.slnx`.

```bash
dotnet build GymHub.slnx
dotnet test GymHub.slnx                                    # functional tests need Docker (Testcontainers)
dotnet test tests/GH.UnitTests --filter "FullyQualifiedName~TrainingProgramTests"
dotnet run --project src/GymHub.AppHost                    # whole system; open the Gateway at https://localhost:7179
```

Angular (`src/GH.WebApp/gh.webapp.ng`): `npm install`, `npm test` (Vitest), `npm run build`. `npm start` runs `scripts/start.mjs`, which serves on Aspire's `PORT`. Styling is Tailwind v4; shared classes (`btn-primary`, `input`, `card`, …) are `@utility` rules in `src/styles.css`.

EF migrations live in each module project; the migration service is the startup project:

```bash
dotnet ef migrations add <Name> --project src/Modules/Programs/GymHub.Modules.Programs --startup-project src/GymHub.MigrationService --context ProgramsDbContext --output-dir Data/Migrations
```

Design-time builds need a connection string even though nothing connects: set `ConnectionStrings__gymhubdb` to any Npgsql string first.

Build settings that bite: **`TreatWarningsAsErrors`** is on (xUnit analyzers included, so pass `TestContext.Current.CancellationToken` in tests), and package versions are central in `Directory.Packages.props`.

### Local secrets and seed data

AppHost user-secrets must hold `Parameters:bff-client-secret`, `Parameters:media-access-key` and `Parameters:media-secret-key`. The bff secret is injected into both Keycloak's realm import and the Gateway.

`src/GymHub.AppHost/Realms/GH-Test-Realm-realm.json` seeds users `member`, `trainer` and `admin` with dev passwords. The file name must be `<realm>-realm.json`. Keycloak imports it only into an empty data volume (`IGNORE_EXISTING`), and Aspire copies it into that volume, so realm changes need a fresh volume.

## Architecture

**Request flow:** Browser → `Gateway` (same origin) → `GymHub.Api`.
- The Gateway serves the SPA (catch-all YARP route to the Angular dev server) and does OIDC code + PKCE login with Keycloak. It keeps tokens in server-side session; the browser holds only a cookie.
- `/gh-api/**` is proxied to `gymhub-api` with the prefix stripped. `GatewayService.AddToken` attaches the bearer token, and `XsrfMiddleware` requires `X-XSRF-TOKEN` (echoed from the `XSRF-TOKEN` cookie). Both match on `Apis[].ApiPath` in `Gateway/appsettings.json`, which must equal the route prefix.
- Gateway endpoints: `/login`, `/logout`, `/api/user` (the SPA's source of identity and roles).
- Keycloak is pinned to `KC_HOSTNAME=http://localhost:8080`, so the issuer matches for the Gateway (direct) and the API (service discovery).

**Module host:** `GymHub.Api/Program.cs` keeps an explicit `IModule[]` list. Each module registers services and maps endpoints under `/api/v1/{module.Name}`. The JWT bearer uses audience `gymhub-api`, `RoleClaimType = "roles"`, and policies `Policies.Trainer` / `Policies.Admin`.

**Module rules** (enforced by `tests/GH.UnitTests/Architecture`):
- A module references `GymHub.BuildingBlocks` and other modules' **`.Contracts`** projects only. For example, Programs uses `IEntitlements` from Subscriptions.Contracts, and Subscriptions uses `IProgramCatalog` from Programs.Contracts.
- Each module owns a `ModuleDbContext` subclass with its own Postgres **schema**, outbox table and migrations, all in the single `gymhubdb` database. There are no cross-module FKs.
- Slices (`Features/<Slice>/<Slice>.cs`: endpoint + handler + request + validator) don't reference each other. Shared code goes in `Features/Shared` or `Domain`.

**BuildingBlocks** (`src/BuildingBlocks/GymHub.BuildingBlocks`):
- **Result type:** handlers return `Result`/`Result<T>`, and `.ToHttp()` maps to ProblemDetails (404/403/409/400/402).
- **Validation:** `.WithValidation<T>()` runs FluentValidation.
- **Users:** `CurrentUser.Required(principal)` gives the Keycloak `sub`.
- **Outbox:** aggregates raise `IntegrationEvent`s, and the module DbContext writes them to its outbox in the same `SaveChanges`. `OutboxDispatcher` delivers them in-process to handlers registered with `AddIntegrationEventHandler<TEvent, THandler>()`, reusing `EventBus`'s keyed-handler scheme. Delivery is at-least-once, so handlers must be idempotent.
- **Storage:** `IObjectStorage` hands out presigned S3 URLs. Browsers upload and download directly, and RustFS CORS allows the Gateway origin.

**Programs module:**
- Model: `TrainingProgram` aggregate → weeks → days → workouts → exercises, with sets stored as JSON.
- Release: each week has a calendar `ReleasesOn` date that applies to every subscriber. Content endpoints return only weeks with `ReleasesOn <= today` (UTC).
- Pricing: price versions; a published program's price change adds a version.
- Invariants live in the domain and return `Error`s.
- `WeekReleaseJob` raises `ProgramWeekReleased` once per released week.

**Trainers module:** trainer applications (admin approval grants the Keycloak `trainer` role through the outbox), trainer profiles, follows, and a basic `MemberProfile` (display name + avatar) for every user. Other modules read names through `IProfileDirectory`; a trainer profile name wins over a member profile name.

**Discussions module:**
- One board per program, for its subscribers (`IEntitlements`), its trainer and admins (`BoardAccess`, checked on every request). Draft programs have no board; archived ones are read-only.
- `DiscussionThread` (Question/General) with `Post`s: the opening post has no parent, replies nest up to `MaxDepth` (8). Deleting a post with replies leaves a placeholder.
- The trainer/admin pins, locks and hides; anyone reports a post; admins review reports at `/discussions/reports`.
- Live updates: `DiscussionsHub` (SignalR) at `/api/v1/discussions/hub`, proxied by the Gateway like any API call. Messages carry ids only (`threadChanged`); clients refetch over REST so per-viewer access applies. In-memory, single instance.
- The Gateway's XSRF check accepts WebSocket handshakes by `Origin` (browsers can't send the header, and HTTP/2 handshakes are CONNECT requests).

**Other projects:** `GymHub.MigrationService` migrates every module and then exits; the API `WaitForCompletion`s it. `EventBusRabbitMQ` is kept but unused.

## Repo tooling

- `.github/instructions/` (dotnet, angular, copilot) describes the conventions above in more depth, and `.github/references/` has review checklists.
- Husky.Net hooks live in `.husky/`; the pre-commit hook only echoes.
