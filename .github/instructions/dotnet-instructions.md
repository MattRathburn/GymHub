# Instructions.md — Modular Monolith with Vertical Slices (.NET 10 + Aspire)

## Overview

GymHub is a **modular monolith**: one API host (`GymHub.Api`) loads independent business modules, and each module is organised into **vertical slices** (one folder per feature). A separate **BFF** (`Gateway`) handles login and sessions in front of it.

* .NET 10 (ASP.NET Core Minimal APIs)
* .NET Aspire (orchestration, service defaults, service discovery)
* PostgreSQL (one database, one schema per module)
* Keycloak (identity; realm checked in as code)
* S3-compatible object storage (RustFS locally) for media
* Angular (frontend, served through the Gateway)

---

## High-Level Architecture

```
Browser (Angular SPA)
      ↓  same origin, session cookie + XSRF header
Gateway (YARP BFF: OIDC login, tokens kept server-side)
      ↓  /gh-api/** with a bearer token
GymHub.Api (module host)
      ↓
Modules: Programs, Subscriptions, … (vertical slices inside each)
      ↓
PostgreSQL (schema per module) · Object storage · Outbox (in-process events)
```

---

## Solution Structure

```
/src
  /GymHub.AppHost            # Aspire host; Realms/ holds the Keycloak realm import
  /GymHub.ServiceDefaults    # Telemetry, health, service discovery, OpenAPI
  /Gateway                   # BFF
  /GymHub.Api                # Module host: explicit list of IModule
  /GymHub.MigrationService   # Runs every module's EF migrations, then exits
  /BuildingBlocks
    /GymHub.BuildingBlocks   # IModule, auth policies, ModuleDbContext, outbox, validation, Result, paging, storage
    /EventBus                # IntegrationEvent + IIntegrationEventHandler (used by the outbox)
    /EventBusRabbitMQ        # Unused until a module is extracted
  /Modules
    /<Module>
      /GymHub.Modules.<Module>            # internal: Domain/, Data/, Features/<Slice>/, <Module>Module.cs
      /GymHub.Modules.<Module>.Contracts  # public: integration events + query interfaces
  /GH.WebApp/gh.webapp.ng    # Angular
/tests
  /GH.UnitTests              # domain rules + architecture rules
  /GH.FunctionalTests        # WebApplicationFactory + Testcontainers Postgres
```

---

## Module Rules

* A module references **BuildingBlocks** and **other modules' `.Contracts` only**, never another module's implementation project. `GH.UnitTests/Architecture` enforces this.
* Each module owns its data: its own `ModuleDbContext` subclass, its own Postgres **schema**, its own migrations. No cross-module joins or foreign keys; refer to other modules' data by id.
* Cross-module **reads** go through a contract interface (e.g. `IProgramCatalog`, `IEntitlements`) implemented by the owning module.
* Cross-module **side effects** go through integration events (see Messaging).
* A module is an `IModule` (`Name`, `AddServices`, `MapEndpoints`) and is added to the explicit list in `GymHub.Api/Program.cs` and to `GymHub.MigrationService/Program.cs`. Its endpoints live under `/api/v1/{Name}`.

---

## Vertical Slice Rules

Each slice is a folder under `Features/` holding its endpoint, request/response records, validator and handler, usually in one file named after the slice:

```
/Features/PublishProgram
  PublishProgram.cs   # PublishProgramHandler + PublishProgramEndpoint (+ request/validator when needed)
```

* Slices do not reference other slices. Anything shared within a module goes in `Features/Shared` (or `Domain`).
* Business rules live in the domain model (e.g. `TrainingProgram`), which returns `Result`/`Error` instead of throwing.
* Handlers are plain classes registered as scoped services; no mediator.

### Endpoint pattern

```csharp
internal static class PublishProgramEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{programId:guid}/publish", async (Guid programId, ClaimsPrincipal user, PublishProgramHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, CurrentUser.Required(user), ct)).ToHttp())
            .RequireAuthorization(Policies.Trainer)
            .WithName("PublishProgram");
}
```

* `Result.ToHttp()` maps errors to ProblemDetails (`NotFound` 404, `Forbidden` 403, `Conflict` 409, `Invalid` 400, `PaymentRequired` 402).
* Validate request bodies with FluentValidation and `.WithValidation<TRequest>()`.
* Enforce auth per endpoint: `RequireAuthorization(Policies.Trainer | Policies.Admin)` for roles, plus ownership checks in the handler.

---

## Persistence (PostgreSQL)

* EF Core, one `ModuleDbContext` per module, registered with `builder.AddModuleDbContext<TContext>(schema)`.
* Domain objects generate their own `Guid` ids (`Guid.CreateVersion7()`); configure child keys with `ValueGeneratedNever()`.
* No generic repositories; query the DbContext inside the slice.
* Migrations live in the module project:

```bash
dotnet ef migrations add <Name> --project src/Modules/<Module>/GymHub.Modules.<Module> --startup-project src/GymHub.MigrationService --context <Module>DbContext --output-dir Data/Migrations
```

---

## Messaging (in-process outbox)

* Integration events are records deriving from `IntegrationEvent`, declared in the publishing module's `.Contracts`.
* Publish by queuing the event on the module's DbContext (`AddIntegrationEvent`, or by raising it from the aggregate and letting the DbContext collect it on save). It is written in the **same transaction** as the state change.
* `OutboxDispatcher` delivers events in-process to handlers registered with `services.AddIntegrationEventHandler<TEvent, THandler>()`. Delivery is at-least-once: **handlers must be idempotent**.
* RabbitMQ (`EventBusRabbitMQ`) is kept for when a module is split out; the event and handler types are the same.

---

## Authentication & Authorization

* Users sign in through the Gateway (OIDC code + PKCE against Keycloak). The browser only holds a session cookie; the Gateway attaches the access token to `/gh-api/**` requests.
* The API validates Keycloak JWTs (audience `gymhub-api`). Realm roles arrive as a flat `roles` claim: `trainer`, `admin`.
* The user id everywhere is the Keycloak `sub` (`CurrentUser.Id`).

---

## Testing Strategy

* Domain rules: unit tests on aggregates (`GH.UnitTests`).
* Architecture rules: `GH.UnitTests/Architecture` (module and slice boundaries).
* Endpoints: functional tests on the real host with Testcontainers Postgres and a header-based test auth scheme (`GH.FunctionalTests`).

---

## Adding a New Feature (Checklist)

1. Create `Features/<Slice>/<Slice>.cs` in the owning module
2. Add request/response records and a validator
3. Put rules in the domain model; keep the handler thin
4. Map the endpoint with auth and validation, and register the handler and endpoint in the module class
5. Publish an integration event if other modules care
6. Add a migration if the model changed
7. Add domain and functional tests
