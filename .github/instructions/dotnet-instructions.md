# Instructions.md — Vertical Slice Architecture (.NET 10 + Aspire)

## Overview

This project follows a **Vertical Slice Architecture** using:

* .NET 10 (ASP.NET Core + Minimal APIs)
* .NET Aspire (orchestration & service defaults)
* PostgreSQL (primary database)
* Angular (frontend)
* RabbitMQ (event-driven messaging)

The goal is to organize the system **by feature**, not by technical layer, enabling:

* High cohesion
* Low coupling
* Faster feature delivery
* Easier testing and maintenance

---

## High-Level Architecture

```
Client (Angular)
      ↓
BFF / API Gateway (Minimal APIs)
      ↓
Feature Slices (Vertical)
      ↓
Infrastructure (PostgreSQL, RabbitMQ)
```

---

## Solution Structure

```
/src
  /AppHost                 # .NET Aspire host
  /ServiceDefaults         # Shared Aspire defaults

  /Api                     # Minimal API entry point (BFF)

  /Features
    /Users
      /CreateUser
      /GetUser
      /Shared
    /Workouts
      /CreateWorkout
      /LogWorkout
      /GetWorkouts

  /BuildingBlocks
    /Persistence
    /Messaging
    /Auth
    /Common

  /Frontend
    /angular-app
```

---

## Vertical Slice Rules

Each feature slice must:

* Be **self-contained**
* Contain its own:

  * Endpoint
  * Request/Response models
  * Validation
  * Business logic
  * Data access
* Not depend on other slices directly

### Example Slice Structure

```
/Features/Workouts/CreateWorkout
  CreateWorkoutEndpoint.cs
  CreateWorkoutRequest.cs
  CreateWorkoutHandler.cs
  CreateWorkoutValidator.cs
  CreateWorkoutResponse.cs
```

---

## Minimal APIs (Endpoint Pattern)

Each slice exposes its own endpoint.

```csharp
public static class CreateWorkoutEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/workouts", async (
            CreateWorkoutRequest request,
            CreateWorkoutHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(request, ct);
            return Results.Ok(result);
        });
    }
}
```

### Endpoint Registration

In `Program.cs`:

```csharp
app.MapWorkoutsEndpoints();
```

---

## Application Logic (Handler Pattern)

Business logic lives inside handlers.

```csharp
public class CreateWorkoutHandler
{
    private readonly AppDbContext _db;
    private readonly IPublishEndpoint _publisher;

    public CreateWorkoutHandler(AppDbContext db, IPublishEndpoint publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public async Task<CreateWorkoutResponse> Handle(CreateWorkoutRequest request, CancellationToken ct)
    {
        var workout = new Workout { Name = request.Name };

        _db.Workouts.Add(workout);
        await _db.SaveChangesAsync(ct);

        await _publisher.Publish(new WorkoutCreatedEvent(workout.Id), ct);

        return new CreateWorkoutResponse(workout.Id);
    }
}
```

---

## PostgreSQL (Persistence)

* Use **Entity Framework Core**
* One shared `AppDbContext`
* Keep entity configuration close to features when possible

### Rules

* No generic repository pattern
* Query directly via DbContext
* Keep queries inside the slice

---

## RabbitMQ (Messaging)

Used for:

* Domain events
* Cross-service communication

### Guidelines

* Publish events from handlers
* Consume events in separate consumers
* Use contracts in `/BuildingBlocks/Messaging`

### Example Event

```csharp
public record WorkoutCreatedEvent(Guid WorkoutId);
```

---

## .NET Aspire

Aspire manages:

* Service discovery
* Configuration
* Observability
* Container orchestration

### AppHost Example

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var rabbit = builder.AddRabbitMQ("rabbitmq");

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(postgres)
    .WithReference(rabbit);

builder.Build().Run();
```

---

## Angular Frontend

* Communicates with API via HTTP
* Organized by feature modules
* Uses services per feature

### Suggested Structure

```
/src/app
  /features
    /workouts
      workouts.service.ts
      create-workout.component.ts
```

---

## Authentication & Authorization

* Use JWT or external provider (e.g., IdentityServer / Keycloak)
* Enforce auth at endpoint level

```csharp
app.MapPost("/workouts", ...)
   .RequireAuthorization();
```

---

## Testing Strategy

* Unit test handlers
* Integration test endpoints
* Use Testcontainers for PostgreSQL & RabbitMQ

---

## Conventions

### Naming

* `Feature + Action` (e.g., `CreateWorkout`)

### Dependency Rules

* Features → BuildingBlocks only
* No feature-to-feature dependencies

### Validation

* Use FluentValidation per slice

---

## Adding a New Feature (Checklist)

1. Create new feature folder
2. Add Request/Response
3. Implement Handler
4. Add Validator
5. Create Endpoint
6. Register endpoint
7. Add tests

---

## Key Principles

* Slice by feature, not layer
* Keep things close together
* Avoid over-abstraction
* Prefer explicit over generic
* Optimize for change

---

## Future Enhancements

* CQRS separation (if needed)
* Caching (Redis)
* Event sourcing (optional)
* Background workers via Aspire

---

## Summary

This architecture enables:

* Faster feature development
* Easier onboarding
* Scalable microservice transition
* Clean separation of concerns

Build features end-to-end, keep slices isolated, and let Aspire handle the infrastructure 🚀
